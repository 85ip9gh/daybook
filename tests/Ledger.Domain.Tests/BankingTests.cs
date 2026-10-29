using Ledger.Domain.Banking;

namespace Ledger.Domain.Tests;

public class BankingTests
{
    private const string Header = "date,reference,description,amount\n";

    private static readonly DateOnly Day = new(2026, 10, 10);
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);

    private static BankItem Bank(int dayOffset, decimal amount) => new(Guid.NewGuid(), Day.AddDays(dayOffset), amount);

    private static LedgerItem Book(int dayOffset, decimal amount) => new(Guid.NewGuid(), 1, Day.AddDays(dayOffset), amount);

    [Fact]
    public void A_statement_parses_into_lines_with_signed_amounts()
    {
        var parsed = StatementParser.Parse(Header + "2026-10-01,DEP-1,Customer deposit,250.00\r\n2026-10-02,FEE-9,Monthly fee,-12.50\n");

        Assert.True(parsed.IsValid);
        Assert.Equal(
            [
                new StatementLineDraft(new DateOnly(2026, 10, 1), "DEP-1", "Customer deposit", 250.00m),
                new StatementLineDraft(new DateOnly(2026, 10, 2), "FEE-9", "Monthly fee", -12.50m),
            ],
            parsed.Lines);
    }

    [Fact]
    public void Quoted_fields_keep_their_commas_quotes_and_line_breaks()
    {
        var parsed = StatementParser.Parse(Header + "2026-10-01,R1,\"Smith, Jones \"\"and\"\" Co\nSecond line\",10.00\n\n2026-10-02,R2,plain,5.00");

        Assert.True(parsed.IsValid);
        Assert.Equal("Smith, Jones \"and\" Co\nSecond line", parsed.Lines[0].Description);
        Assert.Equal("R2", parsed.Lines[1].Reference);
    }

    [Fact]
    public void Each_bad_line_is_reported_with_its_line_number_and_nothing_is_kept()
    {
        var parsed = StatementParser.Parse(
            Header
            + "2026-10-01,R1,fine,10.00\n"
            + "01/10/2026,R2,bad date,10.00\n"
            + "2026-10-01,,no reference,10.00\n"
            + "2026-10-01,R4,zero,0\n"
            + "2026-10-01,R5,three places,1.005\n"
            + "2026-10-01,R6,too few\n"
            + "2026-10-01,r1,repeated reference,3.00\n");

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Lines);
        Assert.Equal([3, 4, 5, 6, 7, 8], parsed.Errors.Select(error => int.Parse(error.Split(':')[0]["Line ".Length..])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("date,reference,amount\n2026-10-01,R1,5.00")]
    [InlineData("date,reference,description,amount\n")]
    [InlineData("date,reference,description,amount\n2026-10-01,R1,\"never closed,5.00")]
    public void A_statement_with_no_usable_content_is_invalid(string content)
    {
        var parsed = StatementParser.Parse(content);

        Assert.False(parsed.IsValid);
        Assert.Empty(parsed.Lines);
    }

    [Fact]
    public void The_error_list_is_capped()
    {
        var content = Header + string.Concat(Enumerable.Range(0, 100).Select(index => $"not-a-date,R{index},x,1.00\n"));

        var parsed = StatementParser.Parse(content);

        Assert.Equal(StatementParser.MaxErrors + 1, parsed.Errors.Count);
    }

    [Fact]
    public void A_bank_line_matches_a_ledger_line_of_the_same_signed_amount_inside_the_window()
    {
        var deposit = Bank(0, 100m);
        var fee = Bank(0, -12.50m);
        var late = Bank(0, 40m);
        var booked = Book(2, 100m);
        var charged = Book(-3, -12.50m);
        var wrongSign = Book(0, 12.50m);
        var tooFar = Book(4, 40m);

        var matches = Matcher.Pair([deposit, fee, late], [booked, charged, wrongSign, tooFar], 3);

        Assert.Equal(
            [new Match(fee.BankLineId, charged.JournalEntryId, 1), new Match(deposit.BankLineId, booked.JournalEntryId, 1)],
            matches);
    }

    [Fact]
    public void A_ledger_line_is_used_once_however_many_bank_lines_could_take_it()
    {
        var bank = new[] { Bank(0, 10m), Bank(0, 10m), Bank(1, 10m) };
        var ledger = new[] { Book(0, 10m), Book(1, 10m) };

        var matches = Matcher.Pair(bank, ledger, 3);

        Assert.Equal(2, matches.Count);
        Assert.Equal(2, matches.Select(match => match.JournalEntryId).Distinct().Count());
        Assert.Equal(2, matches.Select(match => match.BankLineId).Distinct().Count());
    }

    [Fact]
    public void Pairing_in_date_order_finds_both_matches_where_nearest_first_would_find_one()
    {
        var early = Bank(-4, 50m);
        var later = Bank(0, 50m);
        var first = Book(-2, 50m);
        var second = Book(2, 50m);

        var matches = Matcher.Pair([later, early], [second, first], 2);

        Assert.Equal(
            [new Match(early.BankLineId, first.JournalEntryId, 1), new Match(later.BankLineId, second.JournalEntryId, 1)],
            matches);
    }

    [Fact]
    public void Pairing_is_the_same_whatever_order_the_inputs_arrive_in()
    {
        var bank = Enumerable.Range(0, 30).Select(index => Bank(index % 7, index % 3 == 0 ? 25m : -8m)).ToList();
        var ledger = Enumerable.Range(0, 30).Select(index => Book(index % 5, index % 2 == 0 ? 25m : -8m)).ToList();

        var forward = Matcher.Pair(bank, ledger, 2);
        var shuffled = Matcher.Pair(bank.AsEnumerable().Reverse(), ledger.OrderBy(item => item.JournalEntryId), 2);

        Assert.Equal(forward, shuffled);
        Assert.NotEmpty(forward);
    }

    [Fact]
    public void A_statement_can_only_be_imported_against_an_active_bank_account()
    {
        var bank = Account.Open("1000", "Bank", AccountType.Asset, "CAD", isBank: true);
        var revenue = Account.Open("4000", "Revenue", AccountType.Revenue, "CAD");
        var receivable = Account.Open("1200", "Accounts receivable", AccountType.Asset, "CAD");
        var closed = Account.Open("1001", "Old bank", AccountType.Asset, "CAD", isBank: true);
        closed.Deactivate();

        var statement = BankStatement.Import(bank, Header + "2026-10-01,R1,x,1.00", "clerk", Now);

        Assert.Equal((StatementStatus.Pending, "clerk", 64), (statement.Status, statement.ImportedBy, statement.ContentHash.Length));
        Assert.Equal("bank_account_invalid", Assert.Throws<LedgerRuleException>(() => BankStatement.Import(revenue, "x", "clerk", Now)).Code);
        Assert.Equal("bank_account_invalid", Assert.Throws<LedgerRuleException>(() => BankStatement.Import(receivable, "x", "clerk", Now)).Code);
        Assert.Equal("bank_account_invalid", Assert.Throws<LedgerRuleException>(() => BankStatement.Import(closed, "x", "clerk", Now)).Code);
        Assert.Equal("statement_empty", Assert.Throws<LedgerRuleException>(() => BankStatement.Import(bank, "  ", "clerk", Now)).Code);
        Assert.Equal("statement_too_large", Assert.Throws<LedgerRuleException>(() => BankStatement.Import(bank, new string('x', BankStatement.MaxContentLength + 1), "clerk", Now)).Code);
    }

    [Fact]
    public void The_same_content_always_hashes_the_same_and_different_content_does_not()
    {
        var bank = Account.Open("1000", "Bank", AccountType.Asset, "CAD", isBank: true);

        var first = BankStatement.Import(bank, Header + "2026-10-01,R1,x,1.00", "clerk", Now);
        var again = BankStatement.Import(bank, Header + "2026-10-01,R1,x,1.00", "someone-else", Now.AddDays(1));
        var other = BankStatement.Import(bank, Header + "2026-10-01,R1,x,1.01", "clerk", Now);

        Assert.Equal(first.ContentHash, again.ContentHash);
        Assert.NotEqual(first.ContentHash, other.ContentHash);
    }

    [Fact]
    public void A_bank_line_matches_only_an_equal_amount_and_only_once()
    {
        var bank = Account.Open("1000", "Bank", AccountType.Asset, "CAD", isBank: true);
        var statement = BankStatement.Import(bank, Header + "2026-10-01,R1,x,25.00", "clerk", Now);
        var line = BankLine.From(statement, new StatementLineDraft(Day, "R1", "x", 25m));
        var entryId = Guid.NewGuid();

        Assert.Equal("bank_line_not_matched", Assert.Throws<LedgerRuleException>(line.Unmatch).Code);
        Assert.Equal("match_amount_differs", Assert.Throws<LedgerRuleException>(() => line.MatchTo(entryId, 1, -25m, "approver", Now)).Code);
        line.MatchTo(entryId, 1, 25m, "approver", Now);
        Assert.Equal("bank_line_already_matched", Assert.Throws<LedgerRuleException>(() => line.MatchTo(Guid.NewGuid(), 1, 25m, "approver", Now)).Code);
        Assert.Equal((entryId, 1, "approver"), (line.MatchedEntryId, line.MatchedLineNumber, line.MatchedBy));

        line.Unmatch();
        Assert.False(line.IsMatched);
    }

    [Theory]
    [InlineData(EntrySide.Debit, 30)]
    [InlineData(EntrySide.Credit, -30)]
    public void Money_into_the_bank_is_a_debit_and_money_out_is_a_credit(EntrySide side, int expected)
    {
        Assert.Equal(expected, BankLine.Signed(side, 30m));
    }
}
