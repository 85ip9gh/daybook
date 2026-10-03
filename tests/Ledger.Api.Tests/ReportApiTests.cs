using System.Net;
using System.Net.Http.Json;
using Ledger.Api.Data;
using Ledger.Api.Endpoints;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class ReportApiTests(LedgerApiFixture api)
{
    private static readonly DateOnly Day = new(2026, 10, 5);

    private async Task<EntryResponse> PostAsync(AccountResponse debit, AccountResponse credit, decimal amount, DateOnly date, string description = "Report entry")
    {
        var request = new PostEntryRequest(date, description, "CAD",
        [
            new PostLineRequest(debit.Id, EntrySide.Debit, amount),
            new PostLineRequest(credit.Id, EntrySide.Credit, amount),
        ]);

        return await api.ReadAsync<EntryResponse>(await api.PostEntryAsync(Guid.NewGuid().ToString("N"), request));
    }

    private Task<T?> GetAsync<T>(string path) => api.Client.GetFromJsonAsync<T>(path, LedgerApiFixture.Json);

    [Fact]
    public async Task A_balance_built_from_a_close_snapshot_equals_the_journal_on_every_side_of_the_close()
    {
        var period = api.NextPeriod();
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        await PostAsync(cash, revenue, 100m, period.End.AddDays(-10));
        await PostAsync(cash, revenue, 20m, period.End);
        await PostAsync(revenue, cash, 5m, period.End.AddDays(-1));
        await api.ClosePeriodAsync(period);
        await PostAsync(cash, revenue, 7m, period.End.AddDays(1));
        await PostAsync(cash, revenue, 3m, period.End.AddDays(9));

        foreach (var asOf in new[] { period.End.AddDays(-11), period.End.AddDays(-1), period.End, period.End.AddDays(1), period.End.AddDays(30) })
        {
            var balance = await api.BalanceAsync(cash.Id, asOf);
            var journal = await api.QueryAsync(db => db.JournalLines
                .Where(line => line.AccountId == cash.Id && line.EntryDate <= asOf)
                .GroupBy(line => line.Side)
                .Select(group => new { Side = group.Key, Total = group.Sum(line => line.Amount) })
                .ToListAsync());

            Assert.Equal(journal.SingleOrDefault(row => row.Side == EntrySide.Debit)?.Total ?? 0m, balance.Debits);
            Assert.Equal(journal.SingleOrDefault(row => row.Side == EntrySide.Credit)?.Total ?? 0m, balance.Credits);
        }

        Assert.Equal(125m, (await api.BalanceAsync(cash.Id, period.End.AddDays(30))).Balance);
        Assert.Equal(1, await api.QueryAsync(db => db.PeriodBalances.CountAsync(balance => balance.PeriodEnd == period.End && balance.AccountId == cash.Id)));
    }

    [Fact]
    public async Task An_account_statement_carries_an_opening_balance_and_a_running_one()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        await PostAsync(cash, revenue, 100m, Day.AddDays(-3), "Before the range");
        await PostAsync(cash, revenue, 40m, Day, "First");
        await PostAsync(revenue, cash, 15m, Day.AddDays(1), "Refund");
        await PostAsync(cash, revenue, 5m, Day.AddDays(2), "Last");
        await PostAsync(cash, revenue, 900m, Day.AddDays(9), "After the range");

        var statement = await GetAsync<AccountStatementResponse>($"/accounts/{cash.Id}/statement?from={Day:yyyy-MM-dd}&to={Day.AddDays(2):yyyy-MM-dd}");
        var credit = await GetAsync<AccountStatementResponse>($"/accounts/{revenue.Id}/statement?from={Day:yyyy-MM-dd}&to={Day.AddDays(2):yyyy-MM-dd}");

        Assert.Equal(100m, statement!.OpeningBalance);
        Assert.Equal(["First", "Refund", "Last"], statement.Lines.Select(line => line.Description));
        Assert.Equal([140m, 125m, 130m], statement.Lines.Select(line => line.Balance));
        Assert.Equal((130m, false), (statement.ClosingBalance, statement.Truncated));
        Assert.Equal((100m, 130m), (credit!.OpeningBalance, credit.ClosingBalance));
    }

    [Fact]
    public async Task An_income_statement_covers_only_its_date_range()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var sales = await api.OpenAccountAsync(AccountType.Revenue);
        var rent = await api.OpenAccountAsync(AccountType.Expense);
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        await PostAsync(cash, sales, 999m, from.AddDays(-1));
        await PostAsync(cash, sales, 300m, from);
        await PostAsync(rent, cash, 120m, to);
        await PostAsync(cash, sales, 777m, to.AddDays(1));

        var report = await GetAsync<IncomeStatementResponse>($"/reports/income-statement?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}");
        var backwards = await api.Client.GetAsync($"/reports/income-statement?from={to:yyyy-MM-dd}&to={from:yyyy-MM-dd}");

        Assert.Equal(300m, report!.Statement.Revenue.Single(line => line.AccountId == sales.Id).Amount);
        Assert.Equal(120m, report.Statement.Expenses.Single(line => line.AccountId == rent.Id).Amount);
        Assert.Equal(report.Statement.TotalRevenue - report.Statement.TotalExpenses, report.NetIncome);
        Assert.Equal("report_range_invalid", await LedgerApiFixture.ProblemCodeAsync(backwards));
    }

    [Fact]
    public async Task The_balance_sheet_of_everything_ever_posted_balances()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var loan = await api.OpenAccountAsync(AccountType.Liability);
        await PostAsync(cash, loan, 5000m, Day);

        var report = await GetAsync<BalanceSheetResponse>("/reports/balance-sheet?asOf=2099-12-31");

        Assert.True(report!.IsBalanced);
        Assert.Equal(report.Sheet.TotalAssets, report.Sheet.TotalLiabilities + report.Sheet.TotalEquity + report.Sheet.CurrentEarnings);
        Assert.Equal(5000m, report.Sheet.Assets.Single(line => line.AccountId == cash.Id).Amount);
        Assert.Equal(5000m, report.Sheet.Liabilities.Single(line => line.AccountId == loan.Id).Amount);
    }

    [Fact]
    public async Task Aged_receivables_are_what_was_owed_on_the_day_asked_about()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var issued = ReceivablesScenario.Today;
        var overdue = await api.ReadAsync<InvoiceResponse>(await scenario.IssueRawAsync((await scenario.DraftAsync(100m)).Id, issued, issued));
        var notYetDue = await api.ReadAsync<InvoiceResponse>(await scenario.IssueRawAsync((await scenario.DraftAsync(50m)).Id, issued, issued.AddDays(30)));
        var voided = await api.ReadAsync<InvoiceResponse>(await scenario.IssueRawAsync((await scenario.DraftAsync(25m)).Id, issued, issued));
        await api.ReadAsync<PaymentResponse>(await scenario.PayRawAsync(ReceivablesScenario.NewKey(), 30m, (overdue.Id, 30m)));
        await api.ReadAsync<InvoiceResponse>(await scenario.VoidRawAsync(voided.Id, issued.AddDays(1)));

        async Task<AgedCustomerResponse?> AgedAsync(DateOnly asOf) =>
            (await GetAsync<AgedReceivablesResponse>($"/reports/aged-receivables?asOf={asOf:yyyy-MM-dd}"))!
                .Customers.SingleOrDefault(customer => customer.CustomerId == scenario.Customer.Id);

        var before = await AgedAsync(issued.AddDays(-1));
        var onTheDay = await AgedAsync(issued);
        var later = await AgedAsync(issued.AddDays(45));

        Assert.Null(before);
        Assert.Equal((145m, 145m), (onTheDay!.Current, onTheDay.Total));
        Assert.Equal((0m, 50m, 70m, 120m), (later!.Current, later.Days1To30, later.Days31To60, later.Total));
        Assert.Equal(later.Total, (await api.BalanceAsync(scenario.Receivable.Id, issued.AddDays(45))).Balance);
    }

    [Fact]
    public async Task The_migration_copies_each_entry_date_onto_lines_that_already_existed()
    {
        var options = new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(api.ConnectionStringFor("LedgerBackfill")).Options;
        await using var db = new LedgerDbContext(options);
        var migrator = db.GetInfrastructure().GetService(typeof(IMigrator)) as IMigrator;
        await migrator!.MigrateAsync("AddBanking");

        var cash = Guid.NewGuid();
        var sales = Guid.NewGuid();
        var entry = Guid.NewGuid();
        await db.Database.ExecuteSqlAsync($"INSERT INTO Accounts (Id, Code, Name, Type, Currency, IsActive) VALUES ({cash}, '1000', 'Cash', 1, 'CAD', 1), ({sales}, '4000', 'Sales', 4, 'CAD', 1)");
        await db.Database.ExecuteSqlAsync($"INSERT INTO JournalEntries (Id, EntryDate, Description, Currency, RecordedAt, PostedBy) VALUES ({entry}, '2026-03-15', 'Old entry', 'CAD', SYSDATETIMEOFFSET(), 'someone')");
        await db.Database.ExecuteSqlAsync($"INSERT INTO JournalLines (JournalEntryId, LineNumber, AccountId, Side, Amount) VALUES ({entry}, 1, {cash}, 1, 10), ({entry}, 2, {sales}, 2, 10)");

        await migrator.MigrateAsync();

        var dates = await db.JournalLines.AsNoTracking().Where(line => line.JournalEntryId == entry).Select(line => line.EntryDate).ToListAsync();
        Assert.Equal([new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 15)], dates);
    }

    [Fact]
    public async Task The_database_refuses_a_line_dated_differently_from_its_entry()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var entry = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => api.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO JournalEntries (Id, EntryDate, Description, Currency, RecordedAt, PostedBy) VALUES ({entry}, '2026-10-01', 'Raw insert', 'CAD', SYSDATETIMEOFFSET(), 'test')");
            return await db.Database.ExecuteSqlAsync($"INSERT INTO JournalLines (JournalEntryId, LineNumber, AccountId, Side, Amount, EntryDate) VALUES ({entry}, 1, {cash.Id}, 1, 5, '2026-10-02')");
        }));

        Assert.Contains("FK_JournalLines_JournalEntries_EntryDate", exception.Message);
    }

    [Fact]
    public async Task A_statement_for_an_unknown_account_is_not_found()
    {
        var response = await api.Client.GetAsync($"/accounts/{Guid.NewGuid()}/statement?from=2026-01-01&to=2026-12-31");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
