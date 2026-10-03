using System.Globalization;
using System.Text;

namespace Ledger.Domain.Banking;

public sealed record StatementLineDraft(DateOnly Date, string Reference, string Description, decimal Amount);

public sealed record ParsedStatement(IReadOnlyList<StatementLineDraft> Lines, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class StatementParser
{
    public const int MaxErrors = 20;
    public const int MaxReferenceLength = 64;
    public const int MaxDescriptionLength = 200;
    public const int AmountScale = 2;

    private static readonly string[] Header = ["date", "reference", "description", "amount"];

    public static ParsedStatement Parse(string content)
    {
        var errors = new List<string>();
        var lines = new List<StatementLineDraft>();
        var rows = ReadRows(content, errors);

        if (rows.Count == 0 || !rows[0].Fields.Select(field => field.Trim().ToLowerInvariant()).SequenceEqual(Header))
        {
            errors.Add($"Line 1: the header must be {string.Join(",", Header)}.");
            return new ParsedStatement([], errors);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (lineNumber, fields) in rows.Skip(1))
        {
            if (errors.Count >= MaxErrors)
            {
                errors.Add("Further errors were not listed.");
                break;
            }

            if (fields.Length != Header.Length)
            {
                errors.Add($"Line {lineNumber}: expected {Header.Length} fields, found {fields.Length}.");
                continue;
            }

            var reference = fields[1].Trim();
            var description = fields[2].Trim();

            if (!DateOnly.TryParseExact(fields[0].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                errors.Add($"Line {lineNumber}: the date must be written yyyy-MM-dd.");
            }
            else if (reference.Length is 0 or > MaxReferenceLength)
            {
                errors.Add($"Line {lineNumber}: the reference must be 1 to {MaxReferenceLength} characters.");
            }
            else if (description.Length > MaxDescriptionLength)
            {
                errors.Add($"Line {lineNumber}: the description holds at most {MaxDescriptionLength} characters.");
            }
            else if (!TryParseAmount(fields[3], out var amount))
            {
                errors.Add($"Line {lineNumber}: the amount must be a non-zero number with at most {AmountScale} decimal places.");
            }
            else if (!seen.Add(reference))
            {
                errors.Add($"Line {lineNumber}: reference {reference} appears more than once.");
            }
            else
            {
                lines.Add(new StatementLineDraft(date, reference, description, amount));
            }
        }

        if (errors.Count == 0 && lines.Count == 0)
        {
            errors.Add("The statement has no lines.");
        }

        return new ParsedStatement(errors.Count == 0 ? lines : [], errors);
    }

    private static bool TryParseAmount(string text, out decimal amount) =>
        decimal.TryParse(text.Trim(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out amount)
        && amount != 0
        && Math.Abs(amount) <= JournalEntry.MaxAmount
        && decimal.Round(amount, AmountScale) == amount;

    private static List<(int LineNumber, string[] Fields)> ReadRows(string content, List<string> errors)
    {
        var rows = new List<(int, string[])>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var hasContent = false;
        var line = 1;
        var rowLine = 1;

        void EndRow()
        {
            if (hasContent)
            {
                fields.Add(field.ToString());
                rows.Add((rowLine, [.. fields]));
            }

            fields.Clear();
            field.Clear();
            hasContent = false;
        }

        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];

            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                    line += character == '\n' ? 1 : 0;
                }
                else if (index + 1 < content.Length && content[index + 1] == '"')
                {
                    field.Append('"');
                    index++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    hasContent = true;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    hasContent = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    line++;
                    rowLine = line;
                    break;
                default:
                    field.Append(character);
                    hasContent = hasContent || !char.IsWhiteSpace(character);
                    break;
            }
        }

        if (quoted)
        {
            errors.Add($"Line {rowLine}: a quoted field is never closed.");
        }

        EndRow();
        return rows;
    }
}
