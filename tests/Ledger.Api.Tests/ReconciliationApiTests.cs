using System.Net;
using System.Net.Http.Json;
using System.Text;
using Ledger.Api.Banking;
using Ledger.Api.Endpoints;
using Ledger.Domain;
using Ledger.Domain.Banking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class ReconciliationApiTests(LedgerApiFixture api)
{
    private const string Header = "date,reference,description,amount\n";

    private static readonly DateOnly Day = new(2026, 10, 10);
    private static readonly DateOnly Later = new(2026, 10, 31);

    private static string Reference() => "R" + Guid.NewGuid().ToString("N")[..10];

    private static string Line(DateOnly date, string reference, decimal amount) => $"{date:yyyy-MM-dd},{reference},Statement line,{amount:0.00}\n";

    private async Task<(AccountResponse Bank, AccountResponse Other)> AccountsAsync() =>
        (await api.OpenAccountAsync(AccountType.Asset), await api.OpenAccountAsync(AccountType.Revenue));

    private async Task<EntryResponse> BookAsync(AccountResponse bank, AccountResponse other, decimal amount, DateOnly date)
    {
        var moneyIn = amount > 0;
        var request = new PostEntryRequest(date, "Bank movement", "CAD",
        [
            new PostLineRequest(bank.Id, moneyIn ? EntrySide.Debit : EntrySide.Credit, Math.Abs(amount)),
            new PostLineRequest(other.Id, moneyIn ? EntrySide.Credit : EntrySide.Debit, Math.Abs(amount)),
        ]);

        return await api.ReadAsync<EntryResponse>(await api.PostEntryAsync(Guid.NewGuid().ToString("N"), request));
    }

    private Task<HttpResponseMessage> UploadRawAsync(HttpClient client, Guid accountId, string content) =>
        client.PostAsync($"/bank-accounts/{accountId}/statements", new StringContent(content, Encoding.UTF8, "text/csv"));

    private async Task<StatementResponse> ImportAsync(Guid accountId, string content)
    {
        var accepted = await api.ReadAsync<StatementResponse>(await UploadRawAsync(api.Clerk, accountId, content));
        return await ProcessedAsync(accepted.Id);
    }

    private async Task<StatementResponse> ProcessedAsync(Guid statementId)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var statement = await api.Client.GetFromJsonAsync<StatementResponse>($"/bank-statements/{statementId}", LedgerApiFixture.Json);
            if (statement!.Status is StatementStatus.Processed or StatementStatus.Failed)
            {
                return statement;
            }

            await Task.Delay(50);
        }

        throw new TimeoutException($"Statement {statementId} was not processed within ten seconds.");
    }

    private async Task<ReconciliationResponse> ReportAsync(Guid accountId) =>
        (await api.Client.GetFromJsonAsync<ReconciliationResponse>($"/bank-accounts/{accountId}/reconciliation?asOf={Later:yyyy-MM-dd}", LedgerApiFixture.Json))!;

    private Task<List<BankLine>> LinesAsync(Guid accountId) =>
        api.QueryAsync(db => db.BankLines.AsNoTracking().Where(line => line.AccountId == accountId).OrderBy(line => line.Reference).ToListAsync());

    [Fact]
    public async Task An_upload_is_accepted_at_once_and_matched_in_the_background()
    {
        var (bank, other) = await AccountsAsync();
        var deposit = await BookAsync(bank, other, 250m, Day);
        var fee = await BookAsync(bank, other, -12.50m, Day.AddDays(1));
        var content = Header + Line(Day.AddDays(2), "A-DEP", 250m) + Line(Day, "B-FEE", -12.50m) + Line(Day, "C-UNKNOWN", 99m);

        var upload = await UploadRawAsync(api.Clerk, bank.Id, content);
        var accepted = await api.ReadAsync<StatementResponse>(upload);
        var statement = await ProcessedAsync(accepted.Id);
        var lines = await LinesAsync(bank.Id);

        Assert.Equal(HttpStatusCode.Accepted, upload.StatusCode);
        Assert.Equal("clerk", accepted.ImportedBy);
        Assert.Equal((StatementStatus.Processed, 3, 3, 2), (statement.Status, statement.LineCount, statement.NewLineCount, statement.MatchedCount));
        Assert.Equal([deposit.Id, fee.Id, null], lines.Select(line => line.MatchedEntryId));
        Assert.Equal([Matcher.Actor, Matcher.Actor, null], lines.Select(line => line.MatchedBy));
    }

    [Fact]
    public async Task Uploading_the_same_file_again_changes_nothing()
    {
        var (bank, other) = await AccountsAsync();
        await BookAsync(bank, other, 40m, Day);
        var content = Header + Line(Day, Reference(), 40m);
        var first = await ImportAsync(bank.Id, content);

        var again = await UploadRawAsync(api.Clerk, bank.Id, content);
        var replayed = await api.ReadAsync<StatementResponse>(again);

        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("true", again.Headers.GetValues("Idempotent-Replay").Single());
        Assert.Equal(first.Id, replayed.Id);
        Assert.Single(await LinesAsync(bank.Id));
        Assert.Equal(1, await api.QueryAsync(db => db.BankStatements.CountAsync(statement => statement.AccountId == bank.Id)));
    }

    [Fact]
    public async Task An_overlapping_statement_adds_only_the_lines_not_seen_before()
    {
        var (bank, _) = await AccountsAsync();
        var shared = Reference();
        await ImportAsync(bank.Id, Header + Line(Day, shared, 10m) + Line(Day, Reference(), 20m));

        var second = await ImportAsync(bank.Id, Header + Line(Day, shared, 10m) + Line(Day.AddDays(1), Reference(), 30m));

        Assert.Equal((StatementStatus.Processed, 2, 1), (second.Status, second.LineCount, second.NewLineCount));
        Assert.Equal(3, (await LinesAsync(bank.Id)).Count);
        Assert.Equal(60m, (await ReportAsync(bank.Id)).BankTotal);
    }

    [Fact]
    public async Task A_file_with_a_bad_line_fails_whole_and_stores_no_lines()
    {
        var (bank, _) = await AccountsAsync();

        var statement = await ImportAsync(bank.Id, Header + Line(Day, Reference(), 10m) + "not-a-date,X,broken,5.00\n");

        Assert.Equal(StatementStatus.Failed, statement.Status);
        Assert.StartsWith("Line 3:", statement.Error);
        Assert.Empty(await LinesAsync(bank.Id));
    }

    [Fact]
    public async Task A_line_outside_the_window_waits_for_a_person_to_match_it()
    {
        var (bank, other) = await AccountsAsync();
        var entry = await BookAsync(bank, other, 75m, Day);
        await ImportAsync(bank.Id, Header + Line(Day.AddDays(10), Reference(), 75m));
        var before = await ReportAsync(bank.Id);
        var line = before.UnmatchedBankLines.Single();

        var matched = await api.Approver.PostAsJsonAsync($"/bank-lines/{line.Id}/match", new MatchRequest(entry.Id, 1), LedgerApiFixture.Json);
        var after = await ReportAsync(bank.Id);
        var unmatched = await api.Approver.PostAsync($"/bank-lines/{line.Id}/unmatch", null);
        var restored = await ReportAsync(bank.Id);

        Assert.Equal((1, 1), (before.UnmatchedBankLines.Count, before.UnreconciledLedgerLines.Count));
        Assert.Equal("approver", (await api.ReadAsync<BankLineResponse>(matched)).MatchedBy);
        Assert.Equal((0, 0), (after.UnmatchedBankLines.Count, after.UnreconciledLedgerLines.Count));
        Assert.Equal(HttpStatusCode.OK, unmatched.StatusCode);
        Assert.Equal((1, 1), (restored.UnmatchedBankLines.Count, restored.UnreconciledLedgerLines.Count));
    }

    [Fact]
    public async Task A_manual_match_is_refused_when_the_amounts_differ_or_the_ledger_line_is_taken()
    {
        var (bank, other) = await AccountsAsync();
        var entry = await BookAsync(bank, other, 60m, Day);
        await ImportAsync(bank.Id, Header + Line(Day, "A", 60m) + Line(Day.AddDays(20), "B", 60m) + Line(Day, "C", 61m));
        var report = await ReportAsync(bank.Id);
        var sameAmount = report.UnmatchedBankLines.Single(line => line.Reference == "B");
        var otherAmount = report.UnmatchedBankLines.Single(line => line.Reference == "C");

        var differs = await api.Approver.PostAsJsonAsync($"/bank-lines/{otherAmount.Id}/match", new MatchRequest(entry.Id, 1), LedgerApiFixture.Json);
        var taken = await api.Approver.PostAsJsonAsync($"/bank-lines/{sameAmount.Id}/match", new MatchRequest(entry.Id, 1), LedgerApiFixture.Json);
        var wrongAccount = await api.Approver.PostAsJsonAsync($"/bank-lines/{sameAmount.Id}/match", new MatchRequest(entry.Id, 2), LedgerApiFixture.Json);
        var byClerk = await api.Clerk.PostAsJsonAsync($"/bank-lines/{sameAmount.Id}/match", new MatchRequest(entry.Id, 1), LedgerApiFixture.Json);

        Assert.Equal("match_amount_differs", await LedgerApiFixture.ProblemCodeAsync(differs));
        Assert.Equal(HttpStatusCode.Conflict, taken.StatusCode);
        Assert.Equal("ledger_line_already_matched", await LedgerApiFixture.ProblemCodeAsync(taken));
        Assert.Equal("ledger_line_not_on_account", await LedgerApiFixture.ProblemCodeAsync(wrongAccount));
        Assert.Equal(HttpStatusCode.Forbidden, byClerk.StatusCode);
    }

    [Fact]
    public async Task What_is_matched_on_the_bank_side_equals_what_is_matched_in_the_books()
    {
        var (bank, other) = await AccountsAsync();
        await BookAsync(bank, other, 500m, Day);
        await BookAsync(bank, other, -120m, Day.AddDays(1));
        await BookAsync(bank, other, 80m, Day.AddDays(2));
        await BookAsync(bank, other, -45m, Day.AddDays(15));
        await ImportAsync(bank.Id, Header + Line(Day, "A", 500m) + Line(Day.AddDays(2), "B", -120m) + Line(Day, "C", -7.25m) + Line(Day, "D", 33m));

        var report = await ReportAsync(bank.Id);

        Assert.Equal(415m, report.BookBalance);
        Assert.Equal(405.75m, report.BankTotal);
        Assert.Equal(["C", "D"], report.UnmatchedBankLines.Select(line => line.Reference));
        Assert.Equal([80m, -45m], report.UnreconciledLedgerLines.Select(line => line.Amount));
        Assert.Equal(report.BankTotal - report.UnmatchedBankTotal, report.BookBalance - report.UnreconciledLedgerTotal);
    }

    [Fact]
    public async Task Parallel_processors_handle_each_statement_exactly_once()
    {
        var statementIds = new List<Guid>();
        for (var index = 0; index < 12; index++)
        {
            var (bank, _) = await AccountsAsync();
            var upload = await UploadRawAsync(api.Clerk, bank.Id, Header + Line(Day, Reference(), 10m + index));
            statementIds.Add((await api.ReadAsync<StatementResponse>(upload)).Id);
        }

        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(DrainAsync)));

        foreach (var id in statementIds)
        {
            var statement = await ProcessedAsync(id);
            Assert.Equal((StatementStatus.Processed, 1, 1), (statement.Status, statement.Attempts, statement.NewLineCount));
        }
    }

    [Fact]
    public async Task Statements_competing_for_the_same_ledger_lines_never_match_one_twice()
    {
        var (bank, other) = await AccountsAsync();
        for (var index = 0; index < 5; index++)
        {
            await BookAsync(bank, other, 10m, Day);
        }

        var uploads = new List<Guid>();
        for (var file = 0; file < 4; file++)
        {
            var content = Header + string.Concat(Enumerable.Range(0, 5).Select(_ => Line(Day, Reference(), 10m)));
            uploads.Add((await api.ReadAsync<StatementResponse>(await UploadRawAsync(api.Clerk, bank.Id, content))).Id);
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DrainAsync)));
        var statements = new List<StatementResponse>();
        foreach (var id in uploads)
        {
            statements.Add(await ProcessedAsync(id));
        }

        var lines = await LinesAsync(bank.Id);
        var matched = lines.Where(line => line.IsMatched).ToList();

        Assert.All(statements, statement => Assert.Equal(StatementStatus.Processed, statement.Status));
        Assert.Equal(20, lines.Count);
        Assert.Equal(5, matched.Count);
        Assert.Equal(5, matched.Select(line => (line.MatchedEntryId, line.MatchedLineNumber)).Distinct().Count());
        Assert.Empty((await ReportAsync(bank.Id)).UnreconciledLedgerLines);
    }

    [Fact]
    public async Task A_statement_abandoned_mid_processing_is_picked_up_again()
    {
        var (bank, _) = await AccountsAsync();
        var content = Header + Line(Day, Reference(), 15m);
        var abandoned = await api.QueryAsync(async db =>
        {
            var account = await db.Accounts.SingleAsync(account => account.Id == bank.Id);
            var statement = BankStatement.Import(account, content, "clerk", DateTimeOffset.UtcNow);
            db.BankStatements.Add(statement);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE BankStatements SET Status = {(byte)StatementStatus.Processing}, Attempts = 1, ClaimedAt = {DateTimeOffset.UtcNow.AddHours(-1)} WHERE Id = {statement.Id}");
            return statement.Id;
        });

        var statement = await ProcessedAsync(abandoned);

        Assert.Equal((StatementStatus.Processed, 2, 1), (statement.Status, statement.Attempts, statement.NewLineCount));
    }

    [Fact]
    public async Task A_statement_is_refused_for_an_account_that_is_not_a_bank_account_and_from_a_viewer()
    {
        var (bank, revenue) = await AccountsAsync();
        var content = Header + Line(Day, Reference(), 10m);

        var notABank = await UploadRawAsync(api.Clerk, revenue.Id, content);
        var empty = await UploadRawAsync(api.Clerk, bank.Id, " ");
        var missing = await UploadRawAsync(api.Clerk, Guid.NewGuid(), content);
        var viewer = await UploadRawAsync(api.Viewer, bank.Id, content);

        Assert.Equal("bank_account_invalid", await LedgerApiFixture.ProblemCodeAsync(notABank));
        Assert.Equal("statement_empty", await LedgerApiFixture.ProblemCodeAsync(empty));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, viewer.StatusCode);
    }

    private async Task DrainAsync()
    {
        var more = true;
        while (more)
        {
            await using var scope = api.Services.CreateAsyncScope();
            more = await scope.ServiceProvider.GetRequiredService<StatementProcessor>().ProcessNextAsync(CancellationToken.None);
        }
    }
}
