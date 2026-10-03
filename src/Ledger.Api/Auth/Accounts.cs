using System.Security.Claims;
using Ledger.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Auth;

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);

public sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider clock)
{
    private static readonly JsonWebTokenHandler Handler = new();

    public IssuedToken Issue(AppUser user)
    {
        var jwt = options.Value;
        var now = clock.GetUtcNow();
        var expires = now + jwt.Lifetime;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtOptions.NameClaim, user.UserName),
        };
        claims.AddRange(user.Roles.Split().Select(role => new Claim(JwtOptions.RoleClaim, role.ToString())));

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.Key(), SecurityAlgorithms.HmacSha256),
        });

        return new IssuedToken(token, expires);
    }
}

public sealed class UserDirectory(LedgerDbContext db, IPasswordHasher<AppUser> hasher)
{
    private static readonly AppUser Nobody = AppUser.Create("nobody", [Roles.Viewer]);
    private static readonly string NobodyHash = new PasswordHasher<AppUser>().HashPassword(Nobody, Guid.NewGuid().ToString());

    public async Task<AppUser> CreateAsync(string? userName, string? password, IReadOnlyList<Roles> roles, CancellationToken cancellationToken)
    {
        AppUser.EnsurePasswordAcceptable(password);
        var user = AppUser.Create(userName, roles);
        user.SetPasswordHash(hasher.HashPassword(user, password!));
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<AppUser?> AuthenticateAsync(string? userName, string? password, CancellationToken cancellationToken)
    {
        var name = userName?.Trim().ToLowerInvariant() ?? string.Empty;
        var user = await db.Users.SingleOrDefaultAsync(user => user.UserName == name, cancellationToken);

        var verdict = hasher.VerifyHashedPassword(user ?? Nobody, user?.PasswordHash ?? NobodyHash, password ?? string.Empty);
        if (user is null || !user.IsActive || verdict == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verdict == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.SetPasswordHash(hasher.HashPassword(user, password!));
            await db.SaveChangesAsync(cancellationToken);
        }

        return user;
    }
}
