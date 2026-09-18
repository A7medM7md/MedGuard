using MedGuard.API.Middleware;
using MedGuard.Application;
using MedGuard.Application.Bases;
using MedGuard.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddInfrastructure(builder.Configuration);

var identityServerAuthority = builder.Configuration["IdentityServer:Authority"]!;
var apiScope = builder.Configuration["IdentityServer:ApiScope"]!;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = identityServerAuthority;
        // No single audience is registered per-API in this setup — Duende emits a
        // static "{issuer}/resources" audience instead. Authorization is enforced
        // by the "ApiScope" policy below (checking the `scope` claim), not by
        // audience matching.
        options.TokenValidationParameters.ValidateAudience = false;
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

app.Run();
