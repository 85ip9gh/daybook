using System.Net;
using System.Net.Http.Json;
using Ledger.Domain.Receivables;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public class InvoiceApiTests(LedgerApiFixture api)
{
    private static readonly DateOnly Today = ReceivablesScenario.Today;

    [Fact]
    public async Task A_draft_does_not_touch_the_ledger()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);

        var draft = await scenario.DraftAsync(19.99m, 3m, 0.14m);

        Assert.Equal(InvoiceStatus.Draft, draft.Status);
        Assert.Null(draft.Number);
        Assert.Equal((59.97m, 8.40m, 68.37m), (draft.Subtotal, draft.Tax, draft.Total));
        Assert.Equal(0m, await scenario.BalanceAsync(scenario.Receivable));
    }

    [Fact]
    public async Task Issuing_numbers_the_invoice_and_posts_receivable_revenue_and_tax()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var draft = await scenario.DraftAsync(19.99m, 3m, 0.14m);

        var issued = await api.ReadAsync<InvoiceResponse>(await scenario.IssueRawAsync(draft.Id));

        Assert.Equal(InvoiceStatus.Issued, issued.Status);
        Assert.NotNull(issued.Number);
        Assert.Equal(68.37m, issued.Outstanding);
        Assert.Equal(68.37m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(59.97m, await scenario.BalanceAsync(scenario.Revenue));
        Assert.Equal(8.40m, await scenario.BalanceAsync(scenario.Tax));
        var entry = await api.Client.GetFromJsonAsync<EntryResponse>($"/journal-entries/{issued.JournalEntryId}", LedgerApiFixture.Json);
        Assert.Equal($"Invoice {issued.Number}", entry!.Description);
    }

    [Fact]
    public async Task Concurrent_issues_of_one_draft_issue_it_once()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var draft = await scenario.DraftAsync(100m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => Task.Run(() => scenario.IssueRawAsync(draft.Id))));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        foreach (var response in responses.Where(response => response.StatusCode != HttpStatusCode.OK))
        {
            Assert.Contains(await LedgerApiFixture.ProblemCodeAsync(response), new[] { "invoice_changed", "invoice_not_draft" });
        }

        Assert.Equal(100m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(InvoiceStatus.Issued, (await scenario.InvoiceAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task Concurrent_issues_of_many_drafts_take_consecutive_numbers()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var drafts = new List<InvoiceResponse>();
        for (var index = 0; index < 20; index++)
        {
            drafts.Add(await scenario.DraftAsync(10m));
        }

        var responses = await Task.WhenAll(drafts.Select(draft => Task.Run(() => scenario.IssueRawAsync(draft.Id))));

        var numbers = new List<int>();
        foreach (var response in responses)
        {
            numbers.Add((await api.ReadAsync<InvoiceResponse>(response)).Number!.Value);
        }

        numbers.Sort();
        Assert.Equal(Enumerable.Range(numbers[0], 20), numbers);
    }

    [Fact]
    public async Task An_issue_that_fails_gives_its_number_back()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var first = await scenario.IssuedAsync(10m);
        var failing = await scenario.DraftAsync(10m);

        var failed = await scenario.IssueRawAsync(failing.Id, Today, Today.AddDays(-1));
        var next = await scenario.IssuedAsync(10m);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, failed.StatusCode);
        Assert.Equal("invoice_due_before_issue", await LedgerApiFixture.ProblemCodeAsync(failed));
        Assert.Equal(first.Number + 1, next.Number);
        Assert.Null((await scenario.InvoiceAsync(failing.Id)).Number);
    }

    [Fact]
    public async Task An_invoice_cannot_be_issued_into_a_closed_period()
    {
        var period = api.NextPeriod();
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var draft = await scenario.DraftAsync(10m);
        await api.ClosePeriodAsync(period);

        var response = await scenario.IssueRawAsync(draft.Id, period.End);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("period_closed", await LedgerApiFixture.ProblemCodeAsync(response));
        Assert.Equal(InvoiceStatus.Draft, (await scenario.InvoiceAsync(draft.Id)).Status);
    }

    [Fact]
    public async Task A_line_against_an_account_that_is_not_revenue_is_refused()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var request = new DraftInvoiceRequest(
            scenario.Customer.Id,
            "CAD",
            scenario.Receivable.Id,
            [new InvoiceLineRequest("Consulting", 1m, 10m, scenario.Bank.Id)]);

        var response = await scenario.DraftRawAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("revenue_account_invalid", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Voiding_an_unpaid_invoice_reverses_its_entry()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(75m);

        var voided = await api.ReadAsync<InvoiceResponse>(await scenario.VoidRawAsync(invoice.Id));

        Assert.Equal(InvoiceStatus.Void, voided.Status);
        Assert.Equal(0m, await scenario.BalanceAsync(scenario.Receivable));
        Assert.Equal(0m, await scenario.BalanceAsync(scenario.Revenue));
    }

    [Fact]
    public async Task An_invoice_with_a_payment_against_it_cannot_be_voided()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(75m);
        await api.ReadAsync<PaymentResponse>(await scenario.PayRawAsync(ReceivablesScenario.NewKey(), 25m, (invoice.Id, 25m)));

        var response = await scenario.VoidRawAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("invoice_not_voidable", await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Invoices_are_listed_by_customer_and_status_with_drafts_first()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var older = await scenario.IssuedAsync(10m);
        var newer = await scenario.IssuedAsync(20m);
        var draft = await scenario.DraftAsync(30m);
        var other = await ReceivablesScenario.CreateAsync(api);
        await other.IssuedAsync(99m);

        var all = await api.Client.GetFromJsonAsync<List<InvoiceResponse>>($"/invoices?customerId={scenario.Customer.Id}", LedgerApiFixture.Json);
        var issued = await api.Client.GetFromJsonAsync<List<InvoiceResponse>>($"/invoices?customerId={scenario.Customer.Id}&status=Issued", LedgerApiFixture.Json);

        Assert.Equal([draft.Id, newer.Id, older.Id], all!.Select(invoice => invoice.Id));
        Assert.Equal([newer.Id, older.Id], issued!.Select(invoice => invoice.Id));
    }

    [Fact]
    public async Task A_customer_code_is_unique()
    {
        var existing = await ReceivablesScenario.RegisterCustomerAsync(api);

        var response = await api.Client.PostAsJsonAsync("/customers", new RegisterCustomerRequest(existing.Code, "Duplicate"), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("customer_code_taken", await LedgerApiFixture.ProblemCodeAsync(response));
    }
}
