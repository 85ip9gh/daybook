namespace Ledger.Domain;

public static class Actor
{
    public const int MaxLength = 100;

    public static string Require(string? actor)
    {
        var name = actor?.Trim() ?? string.Empty;
        if (name.Length is 0 or > MaxLength)
        {
            throw new LedgerRuleException("actor_required", "Every change to the books is attributed to a named user.");
        }

        return name;
    }

    public static bool Same(string? left, string? right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}

public sealed class SegregationOfDutiesException(string message) : Exception(message)
{
    public const string Code = "segregation_of_duties";
}
