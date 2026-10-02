using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Api.Data;
using Ledger.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.MsSql;

namespace Ledger.Api.Tests;

public sealed class LedgerApiFixture : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly MsSqlContainer _sql = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
    private WebApplicationFactory<Program>? _factory;
    private int _accountSequence;
    private int _periodSequence;

    public HttpClient Client { get; private set; } = null!;

    public Period NextPeriod()
    {
        var index = Interlocked.Increment(ref _periodSequence) - 1;
        return new Period(2001 + index / 12, index % 12 + 1);
    }

    public Task<HttpResponseMessage> ClosePeriodAsync(Period period) =>
        Client.PostAsync($"/periods/{period.Year}/{period.Month}/close", null);

    public async Task InitializeAsync()
    {
        await _sql.StartAsync();

        var connectionString = new SqlConnectionStringBuilder(_sql.GetConnectionString()) { InitialCatalog = "Ledger" }.ConnectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
            host.UseSetting("ConnectionStrings:Ledger", connectionString));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
        }

        Client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await _sql.DisposeAsync();
    }

    public async Task<T> QueryAsync<T>(Func<LedgerDbContext, Task<T>> query)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<LedgerDbContext>());
    }

    public async Task<AccountResponse> OpenAccountAsync(AccountType type, string currency = "CAD")
    {
        var code = $"T{Interlocked.Increment(ref _accountSequence):D6}";
        var response = await Client.PostAsJsonAsync("/accounts", new OpenAccountRequest(code, $"Account {code}", type, currency), Json);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AccountResponse>(Json))!;
    }

    public Task<HttpResponseMessage> PostEntryAsync(string? idempotencyKey, PostEntryRequest request)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/journal-entries") { Content = JsonContent.Create(request, options: Json) };
        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return Client.SendAsync(message);
    }

    public async Task<AccountBalanceResponse> BalanceAsync(Guid accountId, DateOnly asOf)
    {
        var balance = await Client.GetFromJsonAsync<AccountBalanceResponse>($"/accounts/{accountId}/balance?asOf={asOf:yyyy-MM-dd}", Json);
        return balance!;
    }

    public static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

public readonly record struct Period(int Year, int Month)
{
    public DateOnly End => new(Year, Month, DateTime.DaysInMonth(Year, Month));
}

[CollectionDefinition(Name)]
public sealed class LedgerApiCollection : ICollectionFixture<LedgerApiFixture>
{
    public const string Name = "ledger-api";
}
