using Ledger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api;

internal static class Idempotency
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayHeader = "Idempotent-Replay";

    public static string? ReadKey(string? header)
    {
        var key = header?.Trim() ?? string.Empty;
        return key.Length is 0 or > IdempotencyRecord.MaxKeyLength ? null : key;
    }

    public static IResult KeyRequired() =>
        Problems.BadRequest(
            "idempotency_key_required",
            $"The {KeyHeader} header is required and holds at most {IdempotencyRecord.MaxKeyLength} characters.");

    public static IResult KeyReused(string key) =>
        Problems.Conflict("idempotency_key_reused", $"{KeyHeader} {key} was already used with a different request.");

    public static Task<IdempotencyRecord?> FindAsync(LedgerDbContext db, string key, CancellationToken cancellationToken) =>
        db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record => record.Key == key, cancellationToken);
}
