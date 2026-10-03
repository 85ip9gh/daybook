using Ledger.Api.Auth;
using Ledger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Endpoints;

public sealed record TokenRequest(string UserName, string Password);

public sealed record TokenResponse(string AccessToken, DateTimeOffset ExpiresAt, IReadOnlyList<Roles> Roles);

public sealed record CreateUserRequest(string UserName, string Password, IReadOnlyList<Roles> Roles);

public sealed record AssignRolesRequest(IReadOnlyList<Roles> Roles);

public sealed record UserResponse(Guid Id, string UserName, IReadOnlyList<Roles> Roles, bool IsActive);

internal static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapPost("/auth/token", TokenAsync).WithTags("Auth").AllowAnonymous();

        var users = routes.MapGroup("/users").WithTags("Auth").RequireAuthorization(Policies.Administer);
        users.MapPost("/", CreateAsync);
        users.MapGet("/", ListAsync);
        users.MapPut("/{id:guid}/roles", AssignAsync);
        users.MapPost("/{id:guid}/deactivate", DeactivateAsync);
        return routes;
    }

    private static async Task<IResult> TokenAsync(TokenRequest request, UserDirectory directory, TokenIssuer issuer, CancellationToken cancellationToken)
    {
        var user = await directory.AuthenticateAsync(request.UserName, request.Password, cancellationToken);
        if (user is null)
        {
            return Results.Problem(
                detail: "The user name or password is wrong.",
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Unauthorized",
                extensions: new Dictionary<string, object?> { ["code"] = "credentials_invalid" });
        }

        var token = issuer.Issue(user);
        return Results.Ok(new TokenResponse(token.AccessToken, token.ExpiresAt, [.. user.Roles.Split()]));
    }

    private static async Task<IResult> CreateAsync(CreateUserRequest request, UserDirectory directory, CancellationToken cancellationToken)
    {
        try
        {
            var user = await directory.CreateAsync(request.UserName, request.Password, request.Roles, cancellationToken);
            return Results.Created($"/users/{user.Id}", ToResponse(user));
        }
        catch (DbUpdateException exception) when (exception.IsUniqueViolation())
        {
            return Problems.Conflict("user_name_taken", "A user with that name already exists.");
        }
    }

    private static async Task<IResult> ListAsync(LedgerDbContext db, CancellationToken cancellationToken)
    {
        var users = await db.Users.AsNoTracking().OrderBy(user => user.UserName).ToListAsync(cancellationToken);
        return Results.Ok(users.Select(ToResponse));
    }

    private static async Task<IResult> AssignAsync(Guid id, AssignRolesRequest request, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(id);
        }

        user.Assign(request.Roles);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(user));
    }

    private static async Task<IResult> DeactivateAsync(Guid id, LedgerDbContext db, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null)
        {
            return NotFound(id);
        }

        user.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToResponse(user));
    }

    private static UserResponse ToResponse(AppUser user) => new(user.Id, user.UserName, [.. user.Roles.Split()], user.IsActive);

    private static IResult NotFound(Guid id) => Problems.NotFound("user_not_found", $"User {id} does not exist.");
}
