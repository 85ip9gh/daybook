using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Ledger.Api.Auth;

[Flags]
public enum Roles
{
    None = 0,
    Viewer = 1,
    Clerk = 2,
    Approver = 4,
    Controller = 8,
    Admin = 16,
}

internal static class RoleSets
{
    private static readonly Roles[] Single = [Roles.Viewer, Roles.Clerk, Roles.Approver, Roles.Controller, Roles.Admin];

    public static IEnumerable<Roles> Split(this Roles roles) => Single.Where(role => roles.HasFlag(role));

    public static Roles? Combine(IReadOnlyList<Roles> roles)
    {
        if (roles.Count == 0 || roles.Any(role => !Single.Contains(role)) || roles.Distinct().Count() != roles.Count)
        {
            return null;
        }

        return roles.Aggregate(Roles.None, (combined, role) => combined | role);
    }
}

internal static class Policies
{
    public const string Read = "read";
    public const string Record = "record";
    public const string Post = "post";
    public const string Close = "close";
    public const string Administer = "administer";

    public static AuthorizationBuilder AddLedgerPolicies(this AuthorizationBuilder builder) =>
        builder
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Read, policy => policy.RequireRole(Names(Roles.Viewer | Roles.Clerk | Roles.Approver | Roles.Controller)))
            .AddPolicy(Record, policy => policy.RequireRole(Names(Roles.Clerk)))
            .AddPolicy(Post, policy => policy.RequireRole(Names(Roles.Approver)))
            .AddPolicy(Close, policy => policy.RequireRole(Names(Roles.Controller)))
            .AddPolicy(Administer, policy => policy.RequireRole(Names(Roles.Admin)));

    public static string Name(this ClaimsPrincipal user) => user.Identity?.Name ?? string.Empty;

    private static string[] Names(Roles roles) => [.. roles.Split().Select(role => role.ToString())];
}
