using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinKeyBytes = 32;
    public const string NameClaim = "name";
    public const string RoleClaim = "role";

    public string Issuer { get; set; } = "ledger";

    public string Audience { get; set; } = "ledger-api";

    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromMinutes(30);

    public bool IsUsable() =>
        Encoding.UTF8.GetByteCount(SigningKey) >= MinKeyBytes
        && Lifetime > TimeSpan.Zero
        && Issuer.Length > 0
        && Audience.Length > 0;

    public SymmetricSecurityKey Key() => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters ValidationParameters() =>
        new()
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = Key(),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = NameClaim,
            RoleClaimType = RoleClaim,
        };
}
