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
        var (status, title, code) = exception switch
        {
            LedgerRuleException rule => (StatusCodes.Status422UnprocessableEntity, "Ledger rule violated", rule.Code),
            SegregationOfDutiesException => (StatusCodes.Status403Forbidden, "Forbidden", SegregationOfDutiesException.Code),
            _ => (0, string.Empty, string.Empty),
        };

        if (status == 0)
        {
            return false;
        }

        httpContext.Response.StatusCode = status;

        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails = new ProblemDetails
            {
                Status = status,
                Title = title,
                Detail = exception.Message,
                Extensions = { ["code"] = code },
            },
        });
    }
}
