using System.Net;
using Ledger.Domain.Receivables;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class PaymentApiTests(LedgerApiFixture api)
{
    private static string NewKey() => ReceivablesScenario.NewKey();

    [Fact]
    public async Task A_partial_payment_leaves_the_invoice_open_and_the_rest_settles_it()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);

        var first = await scenario.PayRawAsync(NewKey(), 40m, (invoice.Id, 40m));
        var afterFirst = await scenario.InvoiceAsync(invoice.Id);
        var second = await scenario.PayRawAsync(NewKey(), 60m, (invoice.Id, 60m));
        var afterSecond = await scenario.InvoiceAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal((InvoiceStatus.Issued, 60m), (afterFirst.Status, afterFirst.Outstanding));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal((InvoiceStatus.Paid, 0m), (afterSecond.Status, afterSecond.Outstanding));
        Assert.Equal(0m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(100m, await scenario.BalanceAsync(scenario.Bank));
    }

    [Fact]
    public async Task One_payment_can_settle_several_invoices()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var first = await scenario.IssuedAsync(100m);
        var second = await scenario.IssuedAsync(80m);

        var payment = await api.ReadAsync<PaymentResponse>(await scenario.PayRawAsync(NewKey(), 150m, (first.Id, 100m), (second.Id, 50m)));

        Assert.Equal(2, payment.Allocations.Count);
        Assert.Equal(InvoiceStatus.Paid, (await scenario.InvoiceAsync(first.Id)).Status);
        Assert.Equal(30m, (await scenario.InvoiceAsync(second.Id)).Outstanding);
        Assert.Equal(30m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(150m, await scenario.BalanceAsync(scenario.Bank));
    }

    [Theory]
    [InlineData(120, 120, "allocation_exceeds_outstanding")]
    [InlineData(50, 40, "payment_not_fully_allocated")]
    public async Task A_payment_that_breaks_an_allocation_rule_posts_nothing(int amount, int allocated, string expected)
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);

        var response = await scenario.PayRawAsync(NewKey(), amount, (invoice.Id, allocated));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expected, await LedgerApiFixture.ProblemCodeAsync(response));
        Assert.Equal(0m, await scenario.BalanceAsync(scenario.Bank));
        Assert.Equal(100m, (await scenario.InvoiceAsync(invoice.Id)).Outstanding);
    }

    [Fact]
    public async Task A_payment_cannot_settle_another_customers_invoice()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);
        var stranger = await ReceivablesScenario.RegisterCustomerAsync(api);

        var response = await scenario.PayRawAsync(NewKey(), stranger.Id, 100m, (invoice.Id, 100m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invoice_of_another_customer", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_draft_invoice_cannot_be_paid()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var draft = await scenario.DraftAsync(100m);

        var response = await scenario.PayRawAsync(NewKey(), 100m, (draft.Id, 100m));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invoice_not_open", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Retrying_a_payment_with_the_same_key_takes_the_money_once()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);
        var key = NewKey();

        var first = await scenario.PayRawAsync(key, 40m, (invoice.Id, 40m));
        var retry = await scenario.PayRawAsync(key, 40m, (invoice.Id, 40m));
        var different = await scenario.PayRawAsync(key, 41m, (invoice.Id, 41m));

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal((await api.ReadAsync<PaymentResponse>(first)).Id, (await api.ReadAsync<PaymentResponse>(retry)).Id);
        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal("idempotency_key_reused", await LedgerApiFixture.ProblemCodeAsync(different));
        Assert.Equal(60m, (await scenario.InvoiceAsync(invoice.Id)).Outstanding);
        Assert.Equal(40m, await scenario.BalanceAsync(scenario.Bank));
    }

    [Fact]
    public async Task Concurrent_payments_cannot_overpay_an_invoice()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => scenario.PayRawAsync(NewKey(), 30m, (invoice.Id, 30m)))));

        var created = responses.Count(response => response.StatusCode == HttpStatusCode.Created);
        foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.Created))
        {
            Assert.Contains(await LedgerApiFixture.ProblemCodeAsync(response), new[] { "invoice_changed", "allocation_exceeds_outstanding" });
        }

        var after = await scenario.InvoiceAsync(invoice.Id);
        Assert.InRange(created, 1, 3);
        Assert.Equal(created * 30m, after.AmountPaid);
        Assert.Equal(100m - created * 30m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(created * 30m, await scenario.BalanceAsync(scenario.Bank));
    }

    [Fact]
    public async Task What_customers_owe_equals_the_receivable_control_account()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var first = await scenario.IssuedAsync(250m);
        var second = await scenario.IssuedAsync(120.50m);
        var third = await scenario.IssuedAsync(75m);
        await scenario.DraftAsync(999m);
        await scenario.PayRawAsync(NewKey(), 300m, (first.Id, 250m), (second.Id, 50m));
        await scenario.VoidRawAsync(third.Id);

        var receivables = await scenario.OutstandingAsync();

        Assert.Equal(70.50m, receivables.Outstanding);
        Assert.Equal([second.Id], receivables.Invoices.Select(invoice => invoice.Id));
        Assert.Equal(receivables.Outstanding, await scenario.BalanceAsync(scenario.Receivable));
    }
}
