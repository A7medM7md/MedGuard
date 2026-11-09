using Duende.IdentityModel;
using Duende.IdentityServer.Models;

namespace MedGuard.IdentityServer;

public static class Config
{
    public const string ApiScopeName = "medguard.api";

    public static IEnumerable<IdentityResource> IdentityResources =>
        new IdentityResource[]
        {
            new IdentityResources.OpenId(),
            new IdentityResources.Profile(),
            // Role isn't part of the standard "profile" scope — exposed as its own
            // resource so the Angular client can request it explicitly and the ID
            // token carries a `role` claim the SPA can branch UI on.
            new IdentityResource("roles", "User roles", new[] { JwtClaimTypes.Role }),
        };

    public static IEnumerable<ApiScope> ApiScopes =>
        new ApiScope[]
        {
            new ApiScope(ApiScopeName, "MedGuard API"),
        };

    // The deployments that existed before configuration was added. Extra origins
    // (e.g. the self-hosted https://medguard.dpdns.org) come from "Spa:Origins".
    private static readonly string[] DefaultSpaOrigins = { "https://medguardd.vercel.app", "http://localhost:4200" };

    /// <summary>
    /// Config keys (all optional):
    ///   Spa:Origins       extra SPA origins, ';' or ',' separated, e.g. "https://medguard.dpdns.org"
    ///   M2M:ClientSecret  secret of the medguard-m2m client (defaults to the dev secret)
    /// </summary>
    public static IEnumerable<Client> GetClients(IConfiguration configuration)
    {
        var spaOrigins = DefaultSpaOrigins
            .Concat((configuration["Spa:Origins"] ?? "")
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(o => o.TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var m2mSecret = configuration["M2M:ClientSecret"];
        if (string.IsNullOrWhiteSpace(m2mSecret)) m2mSecret = "medguard-m2m-dev-secret";

        return new Client[]
        {
            // Machine-to-machine: server-side scripts/health checks and, later, an
            // IoT gateway's own service account (separate from a future per-device
            // key scheme). Not used by the Angular SPA.
            new Client
            {
                ClientId = "medguard-m2m",
                ClientName = "MedGuard Service Client",
                AllowedGrantTypes = GrantTypes.ClientCredentials,
                ClientSecrets = { new Secret(m2mSecret.Sha256()) },
                AllowedScopes = { ApiScopeName },
            },

            // The Angular SPA. Public client (no secret — a secret embedded in
            // client-side JS isn't a secret) using Authorization Code + PKCE, the
            // only flow considered safe for browser-based apps.
            new Client
            {
                ClientId = "medguard-angular",
                ClientName = "MedGuard Dashboard",

                AllowedGrantTypes = GrantTypes.Code,
                RequireClientSecret = false,
                RequirePkce = true,

                RedirectUris = spaOrigins.SelectMany(o => new[] { o + "/", o + "/index.html" }).ToList(),
                PostLogoutRedirectUris = spaOrigins.Select(o => o + "/").ToList(),
                // Must be bare origins (scheme+host+port), no path or trailing slash — Duende
                // matches this against the browser's literal `Origin` header, which never has
                // one. A trailing slash here silently breaks CORS for the deployed SPA.
                AllowedCorsOrigins = spaOrigins.ToList(),

                AllowedScopes = { "openid", "profile", "roles", ApiScopeName, "offline_access" },
                AllowOfflineAccess = true,

                AccessTokenLifetime = 3600,
                RefreshTokenUsage = TokenUsage.ReUse,
                RequireConsent = false,

                // By default Duende only puts standard OIDC claims (sub, sid, ...) in the
                // ID token and expects the client to call /connect/userinfo for the rest.
                // This SPA just wants the display name/role for the header immediately
                // after login, so it's simpler to have them ride along on the ID token.
                AlwaysIncludeUserClaimsInIdToken = true,
            },
        };
    }
}
