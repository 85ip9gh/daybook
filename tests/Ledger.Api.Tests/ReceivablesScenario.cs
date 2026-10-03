using System.Net.Http.Json;
using Ledger.Domain;

namespace Ledger.Api.Tests;

internal sealed class ReceivablesScenario
{
    public static readonly DateOnly Today = new(2026, 10, 1);

    private readonly LedgerApiFixture _api;

    private ReceivablesScenario(LedgerApiFixture api, AccountResponse receivable, AccountResponse revenue, AccountResponse tax, AccountResponse bank, CustomerResponse customer)
    {
        _api = api;
        Receivable = receivable;
        Revenue = revenue;
        Tax = tax;
        Bank = bank;
        Customer = customer;
    }

    public AccountResponse Receivable { get; }

    public AccountResponse Revenue { get; }

    public AccountResponse Tax { get; }

    public AccountResponse Bank { get; }

    public CustomerResponse Customer { get; }

    public static async Task<ReceivablesScenario> CreateAsync(LedgerApiFixture api) =>
        new(
            api,
            await api.OpenAccountAsync(AccountType.Asset),
            await api.OpenAccountAsync(AccountType.Revenue),
            await api.OpenAccountAsync(AccountType.Liability),
            await api.OpenAccountAsync(AccountType.Asset),
            await RegisterCustomerAsync(api));

    public static async Task<CustomerResponse> RegisterCustomerAsync(LedgerApiFixture api)
    {
        var code = Guid.NewGuid().ToString("N")[..12];
        var response = await api.Client.PostAsJsonAsync("/customers", new RegisterCustomerRequest(code, $"Customer {code}"), LedgerApiFixture.Json);
        return await api.ReadAsync<CustomerResponse>(response);
    }

    public Task<HttpResponseMessage> DraftRawAsync(DraftInvoiceRequest request) =>
        _api.Client.PostAsJsonAsync("/invoices", request, LedgerApiFixture.Json);

    public async Task<InvoiceResponse> DraftAsync(decimal unitPrice, decimal quantity = 1m, decimal? taxRate = null)
    {
        var request = new DraftInvoiceRequest(
            Customer.Id,
            "CAD",
            Receivable.Id,
            [new InvoiceLineRequest("Consulting", quantity, unitPrice, Revenue.Id)],
            taxRate is null ? null : new InvoiceTaxRequest(taxRate.Value, Tax.Id));

        return await _api.ReadAsync<InvoiceResponse>(await DraftRawAsync(request));
    }

    public Task<HttpResponseMessage> IssueRawAsync(Guid invoiceId, DateOnly? issueDate = null, DateOnly? dueDate = null)
    {
        var issued = issueDate ?? Today;
        return _api.Client.PostAsJsonAsync($"/invoices/{invoiceId}/issue", new IssueInvoiceRequest(issued, dueDate ?? issued.AddDays(30)), LedgerApiFixture.Json);
    }

    public async Task<InvoiceResponse> IssuedAsync(decimal amount)
    {
        var draft = await DraftAsync(amount);
        return await _api.ReadAsync<InvoiceResponse>(await IssueRawAsync(draft.Id));
    }

    public Task<HttpResponseMessage> VoidRawAsync(Guid invoiceId, DateOnly? voidDate = null) =>
        _api.Client.PostAsJsonAsync($"/invoices/{invoiceId}/void", new VoidInvoiceRequest(voidDate ?? Today), LedgerApiFixture.Json);

    public Task<HttpResponseMessage> PayRawAsync(string? key, decimal amount, params (Guid InvoiceId, decimal Amount)[] allocations) =>
        PayRawAsync(key, Customer.Id, amount, allocations);

    public Task<HttpResponseMessage> PayRawAsync(string? key, Guid customerId, decimal amount, params (Guid InvoiceId, decimal Amount)[] allocations)
    {
        var request = new ReceivePaymentRequest(
            customerId,
            Today,
            amount,
            "CAD",
            Bank.Id,
            [.. allocations.Select(allocation => new AllocationRequest(allocation.InvoiceId, allocation.Amount))]);

        var message = new HttpRequestMessage(HttpMethod.Post, "/payments") { Content = JsonContent.Create(request, options: LedgerApiFixture.Json) };
        if (key is not null)
        {
            message.Headers.Add("Idempotency-Key", key);
        }

        return _api.Client.SendAsync(message);
    }

    public async Task<InvoiceResponse> InvoiceAsync(Guid invoiceId) =>
        (await _api.Client.GetFromJsonAsync<InvoiceResponse>($"/invoices/{invoiceId}", LedgerApiFixture.Json))!;

    public async Task<ReceivablesResponse> OutstandingAsync() =>
        (await _api.Client.GetFromJsonAsync<ReceivablesResponse>($"/customers/{Customer.Id}/receivables", LedgerApiFixture.Json))!;

    public async Task<decimal> BalanceAsync(AccountResponse account) => (await _api.BalanceAsync(account.Id, Today)).Balance;

    public static string NewKey() => Guid.NewGuid().ToString("N");
}
