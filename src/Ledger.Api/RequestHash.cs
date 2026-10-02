using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Ledger.Api;

internal static class RequestHash
{
    private const string AmountFormat = "0.############################";

    public static string Of(PostEntryRequest request)
    {
        var description = request.Description.Trim();
        var canonical = new StringBuilder()
            .Append(request.EntryDate.ToString("O", CultureInfo.InvariantCulture)).Append('\n')
            .Append(description.Length).Append(':').Append(description).Append('\n')
            .Append(request.Currency.Trim().ToUpperInvariant()).Append('\n');

        foreach (var line in request.Lines)
        {
            canonical
                .Append(line.AccountId.ToString("N")).Append('|')
                .Append((int)line.Side).Append('|')
                .Append(line.Amount.ToString(AmountFormat, CultureInfo.InvariantCulture)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }
}
