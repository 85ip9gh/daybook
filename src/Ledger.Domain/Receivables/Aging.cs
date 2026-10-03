namespace Ledger.Domain.Receivables;

public enum AgingBucket
{
    Current,
    Days1To30,
    Days31To60,
    Days61To90,
    Over90,
}

public readonly record struct OpenInvoice(Guid CustomerId, DateOnly DueDate, decimal Outstanding);

public sealed record AgedCustomer(Guid CustomerId, decimal Current, decimal Days1To30, decimal Days31To60, decimal Days61To90, decimal Over90)
{
    public decimal Total => Current + Days1To30 + Days31To60 + Days61To90 + Over90;
}

public static class Aging
{
    public static AgingBucket BucketFor(DateOnly dueDate, DateOnly asOf) =>
        (asOf.DayNumber - dueDate.DayNumber) switch
        {
            <= 0 => AgingBucket.Current,
            <= 30 => AgingBucket.Days1To30,
            <= 60 => AgingBucket.Days31To60,
            <= 90 => AgingBucket.Days61To90,
            _ => AgingBucket.Over90,
        };

    public static IReadOnlyList<AgedCustomer> Age(IEnumerable<OpenInvoice> invoices, DateOnly asOf) =>
        [.. invoices
            .Where(invoice => invoice.Outstanding > 0)
            .GroupBy(invoice => invoice.CustomerId)
            .Select(group =>
            {
                decimal In(AgingBucket bucket) => group.Where(invoice => BucketFor(invoice.DueDate, asOf) == bucket).Sum(invoice => invoice.Outstanding);

                return new AgedCustomer(
                    group.Key,
                    In(AgingBucket.Current),
                    In(AgingBucket.Days1To30),
                    In(AgingBucket.Days31To60),
                    In(AgingBucket.Days61To90),
                    In(AgingBucket.Over90));
            })
            .OrderBy(customer => customer.CustomerId)];
}
