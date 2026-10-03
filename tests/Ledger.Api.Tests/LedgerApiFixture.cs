using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Api.Endpoints;
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

    public const string Operator = "operator";

    public string Password { get; } = Guid.NewGuid().ToString("N");

    public string SigningKey { get; } = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

    public HttpClient Client { get; private set; } = null!;

    public HttpClient Clerk { get; private set; } = null!;

    public HttpClient Approver { get; private set; } = null!;

    public HttpClient Controller { get; private set; } = null!;

    public HttpClient Viewer { get; private set; } = null!;

    public HttpClient Admin { get; private set; } = null!;

    public HttpClient Anonymous { get; private set; } = null!;

    public IServiceProvider Services => _factory!.Services;

    public HttpClient NewClient(string? bearer = null)
    {
        var client = _factory!.CreateClient();
        if (bearer is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        }

        return client;
    }

    public async Task CreateUserAsync(string userName, params Roles[] roles)
    {
        await using var scope = _factory!.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<UserDirectory>().CreateAsync(userName, Password, roles, CancellationToken.None);
    }

    public Task<HttpResponseMessage> TokenRawAsync(string userName, string password) =>
        Anonymous.PostAsJsonAsync("/auth/token", new TokenRequest(userName, password), Json);

    public async Task<HttpClient> SignInAsync(string userName)
    {
        var token = await ReadAsync<TokenResponse>(await TokenRawAsync(userName, Password));
        return NewClient(token.AccessToken);
    }

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
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host
            .UseSetting("ConnectionStrings:Ledger", connectionString)
            .UseSetting("Jwt:SigningKey", SigningKey)
            .UseSetting("Reconciliation:PollInterval", "00:00:00.100"));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
        }

        Anonymous = NewClient();

        await CreateUserAsync(Operator, Roles.Clerk, Roles.Approver, Roles.Controller);
        await CreateUserAsync("clerk", Roles.Clerk);
        await CreateUserAsync("approver", Roles.Approver);
        await CreateUserAsync("controller", Roles.Controller);
        await CreateUserAsync("viewer", Roles.Viewer);
        await CreateUserAsync("admin", Roles.Admin);

        Client = await SignInAsync(Operator);
        Clerk = await SignInAsync("clerk");
        Approver = await SignInAsync("approver");
        Controller = await SignInAsync("controller");
        Viewer = await SignInAsync("viewer");
        Admin = await SignInAsync("admin");
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

    public async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
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
