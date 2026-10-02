using System.Net;
using System.Net.Http.Json;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class JournalApiTests(LedgerApiFixture api)
{
    private static readonly DateOnly EntryDate = new(2026, 10, 1);

    private static PostEntryRequest Sale(AccountResponse cash, AccountResponse revenue, decimal amount, string description = "Cash sale") =>
        new(EntryDate, description, "CAD",
        [
            new PostLineRequest(cash.Id, EntrySide.Debit, amount),
            new PostLineRequest(revenue.Id, EntrySide.Credit, amount),
        ]);

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static async Task<EntryResponse> EntryOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<EntryResponse>(LedgerApiFixture.Json))!;

    [Fact]
    public async Task A_posted_entry_is_stored_and_moves_both_balances()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);

        var response = await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 250.75m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var posted = await EntryOf(response);
        var fetched = await api.Client.GetFromJsonAsync<EntryResponse>(response.Headers.Location, LedgerApiFixture.Json);
        Assert.Equal(posted.Id, fetched!.Id);
        Assert.Equal([(1, EntrySide.Debit, 250.75m), (2, EntrySide.Credit, 250.75m)], fetched.Lines.Select(line => (line.LineNumber, line.Side, line.Amount)));
        Assert.Equal(250.75m, (await api.BalanceAsync(cash.Id, EntryDate)).Balance);
        Assert.Equal(250.75m, (await api.BalanceAsync(revenue.Id, EntryDate)).Balance);
    }

    [Fact]
    public async Task A_balance_ignores_entries_dated_after_the_as_of_date()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 40m));

        var before = await api.BalanceAsync(cash.Id, EntryDate.AddDays(-1));

        Assert.Equal(0m, before.Balance);
    }

    [Fact]
    public async Task Retrying_with_the_same_key_returns_the_original_entry_and_posts_nothing()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var key = NewKey();
        var request = Sale(cash, revenue, 100m);

        var first = await api.PostEntryAsync(key, request);
        var retry = await api.PostEntryAsync(key, request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal("true", retry.Headers.GetValues("Idempotent-Replay").Single());
        Assert.Equal((await EntryOf(first)).Id, (await EntryOf(retry)).Id);
        Assert.Equal(100m, (await api.BalanceAsync(cash.Id, EntryDate)).Balance);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_a_conflict()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var key = NewKey();
        await api.PostEntryAsync(key, Sale(cash, revenue, 100m));

        var response = await api.PostEntryAsync(key, Sale(cash, revenue, 101m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_key_reused", await LedgerApiFixture.ProblemCodeAsync(response));
        Assert.Equal(100m, (await api.BalanceAsync(cash.Id, EntryDate)).Balance);
    }

    [Fact]
    public async Task Concurrent_retries_of_one_key_post_exactly_one_entry()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var key = NewKey();
        var request = Sale(cash, revenue, 75m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => api.PostEntryAsync(key, request))));

        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        var ids = await Task.WhenAll(responses.Select(async response => (await EntryOf(response)).Id));
        Assert.Single(ids.Distinct());
        Assert.Equal(1, await api.QueryAsync(db => db.JournalLines.CountAsync(line => line.AccountId == cash.Id)));
        Assert.Equal(75m, (await api.BalanceAsync(cash.Id, EntryDate)).Balance);
    }

    [Fact]
    public async Task A_post_without_an_idempotency_key_is_refused()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);

        var response = await api.PostEntryAsync(null, Sale(cash, revenue, 10m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task An_unbalanced_entry_is_refused_and_leaves_no_trace()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var key = NewKey();
        var request = new PostEntryRequest(EntryDate, "Lopsided", "CAD",
        [
            new PostLineRequest(cash.Id, EntrySide.Debit, 10m),
            new PostLineRequest(revenue.Id, EntrySide.Credit, 9m),
        ]);

        var response = await api.PostEntryAsync(key, request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("entry_unbalanced", await LedgerApiFixture.ProblemCodeAsync(response));
        Assert.Equal(0, await api.QueryAsync(db => db.JournalLines.CountAsync(line => line.AccountId == cash.Id)));
        Assert.False(await api.QueryAsync(db => db.IdempotencyRecords.AnyAsync(record => record.Key == key)));
    }

    [Fact]
    public async Task A_post_against_an_inactive_account_is_refused()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        (await api.Client.PostAsync($"/accounts/{cash.Id}/deactivate", null)).EnsureSuccessStatusCode();

        var response = await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 10m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("account_inactive", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_body_missing_its_lines_is_a_bad_request()
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/journal-entries")
        {
            Content = JsonContent.Create(new { entryDate = "2026-10-01", description = "No lines", currency = "CAD" }),
        };
        message.Headers.Add("Idempotency-Key", NewKey());

        var response = await api.Client.SendAsync(message);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_reversal_returns_both_balances_to_zero_and_keeps_the_original()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var original = await EntryOf(await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 60m)));

        var response = await api.Client.PostAsJsonAsync($"/journal-entries/{original.Id}/reversal", new ReverseEntryRequest(EntryDate.AddDays(2)), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(original.Id, (await EntryOf(response)).ReversesEntryId);
        Assert.Equal(60m, (await api.BalanceAsync(cash.Id, EntryDate.AddDays(1))).Balance);
        Assert.Equal(0m, (await api.BalanceAsync(cash.Id, EntryDate.AddDays(2))).Balance);
        Assert.Equal(0m, (await api.BalanceAsync(revenue.Id, EntryDate.AddDays(2))).Balance);
        Assert.Equal(2, await api.QueryAsync(db => db.JournalLines.CountAsync(line => line.AccountId == cash.Id)));
    }

    [Fact]
    public async Task Concurrent_reversals_of_one_entry_produce_exactly_one_reversal()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var original = await EntryOf(await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 30m)));

        var responses = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            api.Client.PostAsJsonAsync($"/journal-entries/{original.Id}/reversal", new ReverseEntryRequest(EntryDate), LedgerApiFixture.Json))));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Equal(15, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(1, await api.QueryAsync(db => db.JournalEntries.CountAsync(entry => entry.ReversesEntryId == original.Id)));
        Assert.Equal(0m, (await api.BalanceAsync(cash.Id, EntryDate)).Balance);
    }

    [Fact]
    public async Task Reversing_an_unknown_entry_is_not_found()
    {
        var response = await api.Client.PostAsJsonAsync($"/journal-entries/{Guid.NewGuid()}/reversal", new ReverseEntryRequest(EntryDate), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_trial_balance_balances_and_carries_the_posted_accounts()
    {
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 500m));

        var trialBalance = await api.Client.GetFromJsonAsync<TrialBalanceResponse>($"/trial-balance?asOf={EntryDate.AddDays(30):yyyy-MM-dd}", LedgerApiFixture.Json);

        Assert.Equal(trialBalance!.TotalDebit, trialBalance.TotalCredit);
        Assert.Equal(500m, trialBalance.Rows.Single(row => row.AccountId == cash.Id).Debit);
        Assert.Equal(500m, trialBalance.Rows.Single(row => row.AccountId == revenue.Id).Credit);
    }
}
