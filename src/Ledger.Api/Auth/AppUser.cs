using System.Text.RegularExpressions;
using Ledger.Domain;

namespace Ledger.Api.Auth;

public sealed partial class AppUser
{
    public const int MaxUserNameLength = 50;
    public const int MinPasswordLength = 12;
    public const int MaxPasswordLength = 256;

    private AppUser()
    {
    }

    public Guid Id { get; private set; }

    public string UserName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public Roles Roles { get; private set; }

    public bool IsActive { get; private set; }

    public static AppUser Create(string? userName, IReadOnlyList<Roles> roles)
    {
        var name = userName?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!UserNamePattern().IsMatch(name))
        {
            throw new LedgerRuleException("user_name_invalid", $"A user name is 3 to {MaxUserNameLength} characters of a-z, 0-9, dot, dash or underscore.");
        }

        return new AppUser { Id = Guid.NewGuid(), UserName = name, Roles = RequireRoles(roles), IsActive = true };
    }

    public static void EnsurePasswordAcceptable(string? password)
    {
        if (password is null || password.Length is < MinPasswordLength or > MaxPasswordLength)
        {
            throw new LedgerRuleException("password_invalid", $"A password is {MinPasswordLength} to {MaxPasswordLength} characters.");
        }
    }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void Assign(IReadOnlyList<Roles> roles) => Roles = RequireRoles(roles);

    public void Deactivate() => IsActive = false;

    private static Roles RequireRoles(IReadOnlyList<Roles> roles) =>
        RoleSets.Combine(roles) ?? throw new LedgerRuleException("roles_invalid", "A user needs at least one role, each named exactly once.");

    [GeneratedRegex("^[a-z0-9._-]{3,50}$")]
    private static partial Regex UserNamePattern();
}
