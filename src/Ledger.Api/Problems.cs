using Ledger.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api;

internal static class Problems
{
    public static IResult BadRequest(string code, string detail) =>
        Create(StatusCodes.Status400BadRequest, "Bad request", code, detail);

    public static IResult NotFound(string code, string detail) =>
        Create(StatusCodes.Status404NotFound, "Not found", code, detail);

    public static IResult Conflict(string code, string detail) =>
        Create(StatusCodes.Status409Conflict, "Conflict", code, detail);

    private static IResult Create(int status, string title, string code, string detail) =>
        Results.Problem(
            detail: detail,
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed class LedgerRuleExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not LedgerRuleException rule)
        {
            return false;
        }

        httpContext.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status422UnprocessableEntity,
                Title = "Ledger rule violated",
                Detail = rule.Message,
                Extensions = { ["code"] = rule.Code },
            },
        });
    }
}
