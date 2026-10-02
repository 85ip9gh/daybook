namespace Ledger.Domain;

public static class CurrencyCode
{
    public static string Parse(string? value)
    {
        var code = value?.Trim().ToUpperInvariant() ?? string.Empty;
        if (code.Length != 3 || !code.All(char.IsAsciiLetterUpper))
        {
            throw new LedgerRuleException("currency_invalid", "Currency must be a three-letter ISO 4217 code.");
        }

        return code;
    }
}
