using System.Net;
using System.Net.Http.Json;
using Ledger.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class AccountApiTests(LedgerApiFixture api)
{
    [Fact]
    public async Task Opening_an_account_with_a_taken_code_is_a_conflict()
    {
        var existing = await api.OpenAccountAsync(AccountType.Asset);

        var response = await api.Client.PostAsJsonAsync("/accounts", new OpenAccountRequest(existing.Code, "Duplicate", AccountType.Asset, "CAD"), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("account_code_taken", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Opening_an_account_with_a_bad_currency_is_refused()
    {
        var response = await api.Client.PostAsJsonAsync("/accounts", new OpenAccountRequest("BADCUR", "Bad currency", AccountType.Asset, "dollars"), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("currency_invalid", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_bank_account_is_marked_and_must_be_an_asset()
    {
        var bank = await api.OpenAccountAsync(AccountType.Asset, isBank: true);
        var plain = await api.OpenAccountAsync(AccountType.Asset);

        var revenue = await api.Client.PostAsJsonAsync("/accounts", new OpenAccountRequest("BANKREV", "Bank revenue", AccountType.Revenue, "CAD", IsBank: true), LedgerApiFixture.Json);

        Assert.Equal((true, false), (bank.IsBank, plain.IsBank));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, revenue.StatusCode);
        Assert.Equal("bank_account_not_asset", await LedgerApiFixture.ProblemCodeAsync(revenue));
    }

    [Fact]
    public async Task A_new_account_has_a_zero_balance_on_its_normal_side()
    {
        var payable = await api.OpenAccountAsync(AccountType.Liability);

        var balance = await api.BalanceAsync(payable.Id, new DateOnly(2026, 10, 1));

        Assert.Equal(0m, balance.Balance);
        Assert.Equal(EntrySide.Credit, balance.NormalSide);
    }

    [Fact]
    public async Task The_database_itself_refuses_a_non_positive_line_amount()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var entryId = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<SqlException>(() => api.QueryAsync(async db =>
        {
            await db.Database.ExecuteSqlAsync($"INSERT INTO JournalEntries (Id, EntryDate, Description, Currency, RecordedAt) VALUES ({entryId}, '2026-10-01', 'Raw insert', 'CAD', SYSDATETIMEOFFSET())");
            return await db.Database.ExecuteSqlAsync($"INSERT INTO JournalLines (JournalEntryId, LineNumber, AccountId, Side, Amount) VALUES ({entryId}, 1, {cash.Id}, 1, -5)");
        }));

        Assert.Contains("CK_JournalLines_Amount_Positive", exception.Message);
    }
}
