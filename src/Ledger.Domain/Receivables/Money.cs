namespace Ledger.Domain.Receivables;

public static class Money
{
    public const int Scale = 2;

    public static decimal Round(decimal amount) => decimal.Round(amount, Scale, MidpointRounding.AwayFromZero);

    public static bool FitsScale(decimal amount) => decimal.Round(amount, Scale) == amount;
}
