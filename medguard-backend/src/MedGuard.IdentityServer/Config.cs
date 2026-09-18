using Duende.IdentityServer.Models;
using Duende.IdentityModel;

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

    public static IEnumerable<Client> Clients =>
        new Client[]
        {
            // Machine-to-machine: server-side scripts/health checks and, later, an
            // IoT gateway's own service account (separate from a future per-device
            // key scheme). Not used by the Angular SPA.
            new Client
            {
                ClientId = "medguard-m2m",
                ClientName = "MedGuard Service Client",
                AllowedGrantTypes = GrantTypes.ClientCredentials,
                ClientSecrets = { new Secret("medguard-m2m-dev-secret".Sha256()) },
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

                RedirectUris = { "http://localhost:4200/", "http://localhost:4200/index.html" },
                PostLogoutRedirectUris = { "http://localhost:4200/" },
                AllowedCorsOrigins = { "http://localhost:4200" },

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
