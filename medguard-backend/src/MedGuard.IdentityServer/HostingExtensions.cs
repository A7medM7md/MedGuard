using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace MedGuard.IdentityServer;

internal static class HostingExtensions
{
    public static WebApplication ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddRazorPages();

        // Container deployments (docker-compose.yml) set DataPath to a mounted volume.
        // Both key rings MUST survive a container restart:
        //  - Duende's signing keys: otherwise every issued token becomes unverifiable.
        //  - ASP.NET Data Protection keys: Duende encrypts the signing keys with them,
        //    and the login cookies depend on them too. Losing them = everyone logged
        //    out AND the persisted signing keys can no longer be decrypted.
        // Unset (IIS/runasp.net, local dev) = the previous defaults: ./keys + machine store.
        var dataPath = builder.Configuration["DataPath"];
        if (!string.IsNullOrWhiteSpace(dataPath))
        {
            builder.Services.AddDataProtection()
                .SetApplicationName("MedGuard.IdentityServer")
                .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "dataprotection-keys")));
        }

        var isBuilder = builder.Services.AddIdentityServer(options =>
            {
                options.Events.RaiseErrorEvents = true;
                options.Events.RaiseInformationEvents = true;
                options.Events.RaiseFailureEvents = true;
                options.Events.RaiseSuccessEvents = true;

                // see https://docs.duendesoftware.com/identityserver/v6/fundamentals/resources/
                options.EmitStaticAudienceClaim = true;

                if (!string.IsNullOrWhiteSpace(dataPath))
                    options.KeyManagement.KeyPath = Path.Combine(dataPath, "signing-keys");

                // Duende's default is SameSite=None, which a cross-site SPA (Vercel ->
                // runasp.net) needs, but browsers drop None cookies on plain HTTP. The
                // self-hosted deployment serves SPA and IdentityServer from ONE origin,
                // so it sets Lax, which works on both HTTP and HTTPS.
                if (Enum.TryParse<SameSiteMode>(builder.Configuration["Cookies:SameSite"], ignoreCase: true, out var sameSite))
                {
                    options.Authentication.CookieSameSiteMode = sameSite;
                    options.Authentication.CheckSessionCookieSameSiteMode = sameSite;
                }

                // Optional. Without it Duende runs in unlicensed (dev/test) mode and
                // logs a warning; the free Community Edition key works here.
                var licenseKey = builder.Configuration["Duende:LicenseKey"];
                if (!string.IsNullOrWhiteSpace(licenseKey))
                    options.LicenseKey = licenseKey;
            })
            .AddTestUsers(TestUsers.Users);

        // in-memory, code config
        isBuilder.AddInMemoryIdentityResources(Config.IdentityResources);
        isBuilder.AddInMemoryApiScopes(Config.ApiScopes);
        isBuilder.AddInMemoryClients(Config.GetClients(builder.Configuration));

        return builder.Build();
    }

    public static WebApplication ConfigurePipeline(this WebApplication app)
    {
        // Behind the reverse proxy IdentityServer lives under a sub-path
        // (https://medguard.dpdns.org/identity). With PathBase set, Duende builds the
        // issuer, the discovery document and every endpoint URL with that prefix.
        // Must run before routing.
        var pathBase = app.Configuration["PathBase"];
        if (!string.IsNullOrWhiteSpace(pathBase))
            app.UsePathBase(pathBase);

        app.UseSerilogRequestLogging();

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseStaticFiles();
        app.UseRouting();
        app.UseIdentityServer();
        app.UseAuthorization();

        app.MapRazorPages()
            .RequireAuthorization();

        return app;
    }
}
