using System.Net;
using System.Net.Http.Json;
using Ledger.Domain;
using Microsoft.EntityFrameworkCore;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class PeriodApiTests(LedgerApiFixture api)
{
    private static PostEntryRequest Sale(AccountResponse cash, AccountResponse revenue, decimal amount, DateOnly entryDate) =>
        new(entryDate, "Cash sale", "CAD",
        [
            new PostLineRequest(cash.Id, EntrySide.Debit, amount),
            new PostLineRequest(revenue.Id, EntrySide.Credit, amount),
        ]);

    private static string NewKey() => Guid.NewGuid().ToString("N");

    private static async Task<EntryResponse> EntryOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<EntryResponse>(LedgerApiFixture.Json))!;

    [Fact]
    public async Task A_closed_period_refuses_posts_dated_inside_it_and_accepts_the_day_after()
    {
        var period = api.NextPeriod();
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);

        var close = await api.ClosePeriodAsync(period);
        var inside = await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 10m, period.End));
        var after = await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 10m, period.End.AddDays(1)));

        Assert.Equal(HttpStatusCode.Created, close.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, inside.StatusCode);
        Assert.Equal("period_closed", await LedgerApiFixture.ProblemCodeAsync(inside));
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
    }

    [Fact]
    public async Task A_retry_of_a_post_that_landed_before_the_close_still_replays()
    {
        var period = api.NextPeriod();
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var key = NewKey();
        var request = Sale(cash, revenue, 10m, period.End);
        var original = await EntryOf(await api.PostEntryAsync(key, request));
        await api.ClosePeriodAsync(period);

        var retry = await api.PostEntryAsync(key, request);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original.Id, (await EntryOf(retry)).Id);
    }

    [Fact]
    public async Task The_books_only_close_forward()
    {
        var earlier = api.NextPeriod();
        var later = api.NextPeriod();

        var first = await api.ClosePeriodAsync(later);
        var again = await api.ClosePeriodAsync(later);
        var backwards = await api.ClosePeriodAsync(earlier);
        var periods = await api.Client.GetFromJsonAsync<PeriodsResponse>("/periods", LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, backwards.StatusCode);
        Assert.Equal("period_already_closed", await LedgerApiFixture.ProblemCodeAsync(backwards));
        Assert.Equal(later.End, periods!.ClosedThrough);
        Assert.Contains(periods.Closes, close => close.PeriodEnd == later.End);
        Assert.DoesNotContain(periods.Closes, close => close.PeriodEnd == earlier.End);
    }

    [Theory]
    [InlineData(2100, 1, "period_not_ended")]
    [InlineData(2001, 13, "period_invalid")]
    public async Task A_period_that_cannot_be_closed_is_refused(int year, int month, string expected)
    {
        var response = await api.ClosePeriodAsync(new Period(year, month));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expected, await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task An_entry_in_a_closed_period_is_reversed_by_an_entry_dated_in_an_open_one()
    {
        var period = api.NextPeriod();
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        var original = await EntryOf(await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 25m, period.End)));
        await api.ClosePeriodAsync(period);

        var inside = await api.Client.PostAsJsonAsync($"/journal-entries/{original.Id}/reversal", new ReverseEntryRequest(period.End), LedgerApiFixture.Json);
        var after = await api.Client.PostAsJsonAsync($"/journal-entries/{original.Id}/reversal", new ReverseEntryRequest(period.End.AddDays(1)), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, inside.StatusCode);
        Assert.Equal("period_closed", await LedgerApiFixture.ProblemCodeAsync(inside));
        Assert.Equal(HttpStatusCode.Created, after.StatusCode);
        Assert.Equal(25m, (await api.BalanceAsync(cash.Id, period.End)).Balance);
        Assert.Equal(0m, (await api.BalanceAsync(cash.Id, period.End.AddDays(1))).Balance);
    }

    [Fact]
    public async Task The_closing_trial_balance_is_frozen_at_the_close()
    {
        var period = api.NextPeriod();
        var cash = await api.OpenAccountAsync(AccountType.Asset);
        var revenue = await api.OpenAccountAsync(AccountType.Revenue);
        await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 80m, period.End));
        await api.ClosePeriodAsync(period);
        await api.PostEntryAsync(NewKey(), Sale(cash, revenue, 5m, period.End.AddDays(1)));

        var closing = await api.Client.GetFromJsonAsync<TrialBalanceResponse>($"/periods/{period.Year}/{period.Month}/trial-balance", LedgerApiFixture.Json);

        Assert.Equal(period.End, closing!.AsOf);
        Assert.Equal(closing.TotalDebit, closing.TotalCredit);
        Assert.Equal(80m, closing.Rows.Single(row => row.AccountId == cash.Id).Debit);
        Assert.Equal(80m, closing.Rows.Single(row => row.AccountId == revenue.Id).Credit);
    }

    [Fact]
    public async Task The_closing_trial_balance_of_a_period_never_closed_is_not_found()
    {
        var response = await api.Client.GetAsync("/periods/1999/1/trial-balance");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Posts_racing_a_close_are_either_inside_the_snapshot_or_refused()
    {
        const int Rounds = 6;
        const int PostsPerRound = 40;
        const decimal Amount = 10m;
        var accepted = 0;
        var refused = 0;

        for (var round = 0; round < Rounds; round++)
        {
            var period = api.NextPeriod();
            var cash = await api.OpenAccountAsync(AccountType.Asset);
            var revenue = await api.OpenAccountAsync(AccountType.Revenue);

            var posts = new List<Task<HttpResponseMessage>>();
            Task<HttpResponseMessage>? close = null;
            for (var index = 0; index < PostsPerRound; index++)
            {
                if (index == PostsPerRound / 2)
                {
                    close = Task.Run(() => api.ClosePeriodAsync(period));
                }

                posts.Add(Task.Run(() => api.PostEntryAsync(NewKey(), Sale(cash, revenue, Amount, period.End))));
            }

            var responses = await Task.WhenAll(posts);
            Assert.Equal(HttpStatusCode.Created, (await close!).StatusCode);

            var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
            foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
            {
                Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
                Assert.Equal("period_closed", await LedgerApiFixture.ProblemCodeAsync(response));
            }

            var journal = (await api.BalanceAsync(cash.Id, period.End)).Debits;
            var snapshot = await api.QueryAsync(db => db.PeriodBalances
                .Where(balance => balance.PeriodEnd == period.End && balance.AccountId == cash.Id)
                .Select(balance => (decimal?)balance.Debits)
                .SingleOrDefaultAsync()) ?? 0m;

            Assert.Equal(created * Amount, journal);
            Assert.Equal(journal, snapshot);

            accepted += created;
            refused += responses.Length - created;
        }

        Assert.Equal(Rounds * PostsPerRound, accepted + refused);
    }
}
