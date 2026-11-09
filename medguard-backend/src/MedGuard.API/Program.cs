using Microsoft.EntityFrameworkCore;
using MedGuard.API.Middleware;
using MedGuard.API.Realtime;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Infrastructure;
using MedGuard.Infrastructure.Persistence;
using MedGuard.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
// Serilog, OpenTelemetry, health checks: same setup as the bridge and worker (ADR 0011).
builder.AddServiceDefaults("medguard-api");

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("oauth2", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.OAuth2,
        Flows = new Microsoft.OpenApi.Models.OpenApiOAuthFlows
        {
            AuthorizationCode = new Microsoft.OpenApi.Models.OpenApiOAuthFlow
            {
                AuthorizationUrl = new Uri($"{builder.Configuration["IdentityServer:Authority"]}/connect/authorize"),
                TokenUrl = new Uri($"{builder.Configuration["IdentityServer:Authority"]}/connect/token"),
                Scopes = new Dictionary<string, string> { { "medguard.api", "MedGuard API" } },
            },
        },
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "oauth2" },
            },
            new[] { "medguard.api" }
        },
    });
});

builder.Services.AddScoped<ResponseHandler>();
builder.Services.AddApplication();
// EF retries transient SQL errors for API requests (ADR 0010); the worker opts out.
builder.Services.AddInfrastructure(builder.Configuration, enableRetryOnFailure: true);

// Readiness = "can serve requests": the database must answer.
var health = builder.Services.AddHealthChecks()
    .AddDbContextCheck<MedGuardDbContext>("sql", tags: [ServiceDefaultsExtensions.Ready],
        // A plain query, not the default CanConnect: on SQL Server, CanConnect retries
        // a failed login for a full minute (it waits for databases being created).
        customTestQuery: (db, ct) => db.Database.SqlQueryRaw<int>("SELECT 1 AS Value").AnyAsync(ct));

var identityServerAuthority = builder.Configuration["IdentityServer:Authority"]!;
var apiScope = builder.Configuration["IdentityServer:ApiScope"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityServerAuthority;

        // Self-hosted in Docker (docker-compose.yml), the public issuer
        // (https://medguard.dpdns.org/identity) would mean a round trip out through
        // Cloudflare and back in through the tunnel, so the signing keys are fetched
        // from IdentityServer's internal address instead, and the public issuer(s)
        // the browser saw are listed explicitly. Unset = the old behaviour (all
        // derived from Authority).
        var metadataAddress = builder.Configuration["IdentityServer:MetadataAddress"];
        if (!string.IsNullOrWhiteSpace(metadataAddress))
        {
            options.MetadataAddress = metadataAddress;
            options.RequireHttpsMetadata = metadataAddress.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }
        var validIssuers = (builder.Configuration["IdentityServer:ValidIssuers"] ?? "")
            .Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (validIssuers.Length > 0)
            options.TokenValidationParameters.ValidIssuers = validIssuers;

        // No single audience is registered per-API in this setup — Duende emits a
        // static "{issuer}/resources" audience instead. Authorization is enforced
        // by the "ApiScope" policy below (checking the `scope` claim), not by
        // audience matching.
        options.TokenValidationParameters.ValidateAudience = false;

        // The browser WebSocket API can't set an Authorization header, so the
        // SignalR client sends the token as ?access_token=... instead. Accept it
        // ONLY on the hub path: query strings end up in access logs, and every
        // other endpoint keeps requiring the header.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments(MonitoringHub.Path))
                    context.Token = token;
                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ApiScope", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.RequireClaim("scope", apiScope);
    });
});

builder.Services.AddCors(conf =>
{
    conf.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyHeader()
        .AllowAnyOrigin()
        .AllowAnyMethod();
    });
});

// Real-time Monitoring feed (ADR 0007). Off unless Kafka is available (dev
// launch profile only): production keeps polling over HTTP (ADR 0001).
builder.Services.Configure<NotifierOptions>(builder.Configuration.GetSection("Notifier"));
var realtimeEnabled = builder.Configuration.GetValue<bool>("Notifier:Enabled");
if (realtimeEnabled)
{
    builder.Services.AddSignalR();
    builder.Services.AddSingleton<NotifierHealth>();
    builder.Services.AddHostedService<KafkaNotifier>();
    health.AddCheck<NotifierHealthCheck>("live-feed", tags: [ServiceDefaultsExtensions.Ready]);
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.OAuthClientId("medguard-angular");
        options.OAuthUsePkce();
    });
}

app.UseCors("AllowAll");

app.UseMedGuardExceptionHandling();

// The Angular SPA, when its build is in wwwroot (the Docker image puts it there;
// local dev runs `ng serve` instead and wwwroot doesn't exist). Kestrel serves
// it directly; hashed bundles are cached forever, index.html never, so a deploy
// is picked up on the next page load.
var spaIndex = app.Environment.WebRootPath is { } webRoot ? Path.Combine(webRoot, "index.html") : null;
var hostSpa = spaIndex is not null && File.Exists(spaIndex);
var spaStaticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var headers = ctx.Context.Response.Headers;
        headers.CacheControl = ctx.File.Name == "index.html"
            ? "no-cache, no-store, must-revalidate"
            : "public, max-age=31536000, immutable";
    },
};
if (hostSpa)
    app.UseStaticFiles(spaStaticFiles);

// Skipped in Development: when the API is launched with both an http and an https
// binding (the default launchSettings profile), redirecting http -> https here
// sends the browser to a DIFFERENT ORIGIN (different port). Per the Fetch spec,
// browsers strip the Authorization header on any cross-origin redirect — so every
// authenticated request silently loses its Bearer token and comes back 401 on the
// https side, even though the original request carried a perfectly valid token.
// The Angular app talks to the http origin directly in dev, so there's nothing to
// redirect to in the first place.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
// Anonymous on purpose: probes don't carry tokens, and the JSON reveals
// component states only, no data.
app.MapHealthEndpoints();
// Not mapped at all when disabled: the client's connect attempt then fails fast
// (404 on /negotiate) and the page falls back to polling.
if (realtimeEnabled)
    app.MapHub<MonitoringHub>(MonitoringHub.Path);

if (hostSpa)
{
    // Unknown /api, /hubs and /health URLs stay 404s instead of returning the SPA's
    // HTML. Every other unmatched path is a client-side route (e.g. /batches/42):
    // serve index.html and let the Angular router take it from there.
    app.MapFallback("/api/{**rest}", () => Results.NotFound());
    app.MapFallback("/hubs/{**rest}", () => Results.NotFound());
    app.MapFallback("/health/{**rest}", () => Results.NotFound());
    app.MapFallbackToFile("index.html", spaStaticFiles);
}

app.Run();
