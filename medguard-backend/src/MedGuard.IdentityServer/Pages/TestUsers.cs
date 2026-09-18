using Duende.IdentityModel;
using System.Security.Claims;
using Duende.IdentityServer.Test;

namespace MedGuard.IdentityServer;

/// <summary>
/// Seeded demo accounts, not a real user store. Fine for a portfolio/graduation
/// project; a production deployment would back this with ASP.NET Core Identity
/// (or another persistent store) instead of Duende's in-memory TestUserStore.
/// </summary>
public static class TestUsers
{
    public static List<TestUser> Users =>
        new()
        {
            new TestUser
            {
                SubjectId = "1",
                Username = "admin@medguard.local",
                Password = "Demo@123",
                Claims =
                {
                    new Claim(JwtClaimTypes.Name, "MedGuard Admin"),
                    new Claim(JwtClaimTypes.Email, "admin@medguard.local"),
                    new Claim(JwtClaimTypes.EmailVerified, "true", ClaimValueTypes.Boolean),
                    new Claim(JwtClaimTypes.Role, "Admin"),
                },
            },
            new TestUser
            {
                SubjectId = "2",
                Username = "operator@medguard.local",
                Password = "Demo@123",
                Claims =
                {
                    new Claim(JwtClaimTypes.Name, "Cold-Chain Operator"),
                    new Claim(JwtClaimTypes.Email, "operator@medguard.local"),
                    new Claim(JwtClaimTypes.EmailVerified, "true", ClaimValueTypes.Boolean),
                    new Claim(JwtClaimTypes.Role, "Operator"),
                },
            },
        };
}
