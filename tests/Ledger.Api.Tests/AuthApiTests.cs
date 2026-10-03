using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Ledger.Api.Auth;
using Ledger.Api.Endpoints;
using Ledger.Domain.Receivables;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Api.Tests;

[Collection(LedgerApiCollection.Name)]
public partial class AuthApiTests(LedgerApiFixture api)
{
    private static readonly string[] PublicRoutes = ["GET /healthz", "GET /openapi/{documentName}.json", "POST /auth/token"];

    private HttpClient ClientFor(string role) => role switch
    {
        "viewer" => api.Viewer,
        "clerk" => api.Clerk,
        "approver" => api.Approver,
        "controller" => api.Controller,
        "admin" => api.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    private static HttpRequestMessage Request(string method, string path) =>
        new(new HttpMethod(method), path)
        {
            Content = method == "GET" ? null : new StringContent("{}", Encoding.UTF8, "application/json"),
        };

    private string Token(
        string? issuer = null,
        string? audience = null,
        string? signingKey = null,
        TimeSpan? expiresIn = null,
        bool signed = true)
    {
        var now = DateTime.UtcNow;
        var expires = now + (expiresIn ?? TimeSpan.FromMinutes(5));

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer ?? "ledger",
            Audience = audience ?? "ledger-api",
            Subject = new ClaimsIdentity([new Claim(JwtOptions.NameClaim, "viewer"), new Claim(JwtOptions.RoleClaim, nameof(Roles.Viewer))]),
            NotBefore = expires.AddHours(-1),
            IssuedAt = expires.AddHours(-1),
            Expires = expires,
            SigningCredentials = signed
                ? new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? api.SigningKey)), SecurityAlgorithms.HmacSha256)
                : null,
        });
    }

    [Fact]
    public async Task Every_route_but_three_refuses_an_anonymous_caller()
    {
        var routes = api.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods
                .Select(method => (Method: method, Pattern: "/" + endpoint.RoutePattern.RawText!.Trim('/'))))
            .Distinct()
            .ToList();

        var open = new List<string>();
        foreach (var (method, pattern) in routes)
        {
            var path = Placeholder().Replace(pattern, match => match.Groups["type"].Value switch
            {
                "guid" => Guid.NewGuid().ToString(),
                "int" => "1",
                _ => "v1",
            });

            var response = await api.Anonymous.SendAsync(Request(method, path));
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                open.Add($"{method} {pattern}");
            }
        }

        Assert.True(routes.Count >= 25, $"Only {routes.Count} routes were found.");
        Assert.Equal(PublicRoutes, open.Order());
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_user_get_the_same_refusal()
    {
        var wrongPassword = await api.TokenRawAsync("viewer", "not-the-password");
        var unknownUser = await api.TokenRawAsync("nobody-here", api.Password);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        Assert.Equal("credentials_invalid", await LedgerApiFixture.ProblemCodeAsync(wrongPassword));
        Assert.Equal("credentials_invalid", await LedgerApiFixture.ProblemCodeAsync(unknownUser));
    }

    [Fact]
    public async Task The_right_password_returns_a_token_carrying_the_users_roles()
    {
        var token = await api.ReadAsync<TokenResponse>(await api.TokenRawAsync(LedgerApiFixture.Operator, api.Password));

        var parsed = new JsonWebToken(token.AccessToken);
        Assert.Equal([Roles.Clerk, Roles.Approver, Roles.Controller], token.Roles);
        Assert.Equal(["Clerk", "Approver", "Controller"], parsed.Claims.Where(claim => claim.Type == JwtOptions.RoleClaim).Select(claim => claim.Value));
        Assert.Equal(LedgerApiFixture.Operator, parsed.GetClaim(JwtOptions.NameClaim).Value);
        Assert.InRange(token.ExpiresAt - DateTimeOffset.UtcNow, TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task A_token_built_like_the_servers_is_accepted()
    {
        var response = await api.NewClient(Token()).GetAsync("/accounts");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("key")]
    [InlineData("unsigned")]
    [InlineData("garbage")]
    public async Task A_token_that_fails_any_one_check_is_refused(string flaw)
    {
        var token = flaw switch
        {
            "expired" => Token(expiresIn: TimeSpan.FromMinutes(-5)),
            "audience" => Token(audience: "some-other-api"),
            "issuer" => Token(issuer: "someone-else"),
            "key" => Token(signingKey: new string('k', 64)),
            "unsigned" => Token(signed: false),
            _ => "not.a.token",
        };

        var response = await api.NewClient(token).GetAsync("/accounts");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("viewer", "GET", "/accounts", true)]
    [InlineData("viewer", "POST", "/invoices", false)]
    [InlineData("viewer", "POST", "/journal-entries", false)]
    [InlineData("clerk", "GET", "/customers", true)]
    [InlineData("clerk", "POST", "/invoices", true)]
    [InlineData("clerk", "POST", "/payments", true)]
    [InlineData("clerk", "POST", "/invoices/8a1c2f0e-0000-4000-8000-000000000001/issue", false)]
    [InlineData("clerk", "POST", "/journal-entries", false)]
    [InlineData("clerk", "POST", "/accounts", false)]
    [InlineData("clerk", "POST", "/periods/2001/1/close", false)]
    [InlineData("approver", "POST", "/journal-entries", true)]
    [InlineData("approver", "POST", "/invoices/8a1c2f0e-0000-4000-8000-000000000001/issue", true)]
    [InlineData("approver", "POST", "/invoices", false)]
    [InlineData("approver", "POST", "/payments", false)]
    [InlineData("approver", "POST", "/periods/2001/1/close", false)]
    [InlineData("controller", "POST", "/accounts", true)]
    [InlineData("controller", "POST", "/journal-entries", false)]
    [InlineData("controller", "POST", "/payments", false)]
    [InlineData("controller", "GET", "/users", false)]
    [InlineData("admin", "GET", "/users", true)]
    [InlineData("admin", "GET", "/accounts", false)]
    [InlineData("admin", "POST", "/journal-entries", false)]
    public async Task Each_role_reaches_its_own_routes_and_no_others(string role, string method, string path, bool allowed)
    {
        var response = await ClientFor(role).SendAsync(Request(method, path));

        if (allowed)
        {
            Assert.DoesNotContain(response.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
        }
        else
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Someone_holding_both_roles_still_cannot_issue_their_own_invoice()
    {
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var request = new DraftInvoiceRequest(scenario.Customer.Id, "CAD", scenario.Receivable.Id, [new InvoiceLineRequest("Work", 1m, 100m, scenario.Revenue.Id)]);
        var draft = await api.ReadAsync<InvoiceResponse>(await api.Client.PostAsJsonAsync("/invoices", request, LedgerApiFixture.Json));
        var issue = new IssueInvoiceRequest(ReceivablesScenario.Today, ReceivablesScenario.Today);

        var own = await api.Client.PostAsJsonAsync($"/invoices/{draft.Id}/issue", issue, LedgerApiFixture.Json);
        var afterOwn = await scenario.InvoiceAsync(draft.Id);
        var postedAfterOwn = await scenario.BalanceAsync(scenario.Receivable);
        var other = await api.Approver.PostAsJsonAsync($"/invoices/{draft.Id}/issue", issue, LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.Forbidden, own.StatusCode);
        Assert.Equal("segregation_of_duties", await LedgerApiFixture.ProblemCodeAsync(own));
        Assert.Equal((InvoiceStatus.Draft, null), (afterOwn.Status, afterOwn.Number));
        Assert.Equal(0m, postedAfterOwn);
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(100m, await scenario.BalanceAsync(scenario.Receivable));
    }

    [Fact]
    public async Task Every_record_names_the_user_who_made_it()
    {
        var period = api.NextPeriod();
        var scenario = await ReceivablesScenario.CreateAsync(api);
        var invoice = await scenario.IssuedAsync(100m);
        var payment = await api.ReadAsync<PaymentResponse>(await scenario.PayRawAsync(ReceivablesScenario.NewKey(), 100m, (invoice.Id, 100m)));
        await api.ClosePeriodAsync(period);

        var issueEntry = await api.Client.GetFromJsonAsync<EntryResponse>($"/journal-entries/{invoice.JournalEntryId}", LedgerApiFixture.Json);
        var paymentEntry = await api.Client.GetFromJsonAsync<EntryResponse>($"/journal-entries/{payment.JournalEntryId}", LedgerApiFixture.Json);
        var periods = await api.Client.GetFromJsonAsync<PeriodsResponse>("/periods", LedgerApiFixture.Json);

        Assert.Equal(("clerk", "approver"), (invoice.DraftedBy, invoice.IssuedBy));
        Assert.Equal("approver", issueEntry!.PostedBy);
        Assert.Equal("clerk", payment.ReceivedBy);
        Assert.Equal("clerk", paymentEntry!.PostedBy);
        Assert.Equal(LedgerApiFixture.Operator, periods!.Closes.Single(close => close.PeriodEnd == period.End).ClosedBy);
    }

    [Fact]
    public async Task An_admin_manages_users_through_their_whole_life()
    {
        var name = "u" + Guid.NewGuid().ToString("N")[..10];
        var password = Guid.NewGuid().ToString("N");

        var created = await api.Admin.PostAsJsonAsync("/users", new CreateUserRequest(name, password, [Roles.Viewer]), LedgerApiFixture.Json);
        var user = await api.ReadAsync<UserResponse>(created);
        var duplicate = await api.Admin.PostAsJsonAsync("/users", new CreateUserRequest(name.ToUpperInvariant(), password, [Roles.Viewer]), LedgerApiFixture.Json);
        var firstToken = await api.ReadAsync<TokenResponse>(await api.TokenRawAsync(name, password));
        var promoted = await api.Admin.PutAsJsonAsync($"/users/{user.Id}/roles", new AssignRolesRequest([Roles.Viewer, Roles.Clerk]), LedgerApiFixture.Json);
        var secondToken = await api.ReadAsync<TokenResponse>(await api.TokenRawAsync(name, password));
        var deactivated = await api.Admin.PostAsync($"/users/{user.Id}/deactivate", null);
        var afterDeactivation = await api.TokenRawAsync(name, password);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("user_name_taken", await LedgerApiFixture.ProblemCodeAsync(duplicate));
        Assert.Equal([Roles.Viewer], firstToken.Roles);
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        Assert.Equal([Roles.Viewer, Roles.Clerk], secondToken.Roles);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, afterDeactivation.StatusCode);
    }

    [Theory]
    [InlineData("ok-name", "short", "Viewer", "password_invalid")]
    [InlineData("no", "a-long-enough-password", "Viewer", "user_name_invalid")]
    [InlineData("ok-name", "a-long-enough-password", "", "roles_invalid")]
    [InlineData("ok-name", "a-long-enough-password", "Viewer,Viewer", "roles_invalid")]
    public async Task A_user_that_breaks_a_rule_is_not_created(string userName, string password, string roles, string expected)
    {
        var parsed = roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Enum.Parse<Roles>).ToList();

        var response = await api.Admin.PostAsJsonAsync("/users", new CreateUserRequest(userName, password, parsed), LedgerApiFixture.Json);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal(expected, await LedgerApiFixture.ProblemCodeAsync(response));
    }

    [Fact]
    public async Task A_stored_password_is_a_hash_and_never_the_password()
    {
        var hash = await api.QueryAsync(db => Task.FromResult(db.Users.Single(user => user.UserName == "viewer").PasswordHash));

        Assert.DoesNotContain(api.Password, hash);
        Assert.True(hash.Length >= 60);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, true)]
    public void A_signing_key_shorter_than_256_bits_is_unusable(int length, bool usable)
    {
        Assert.Equal(usable, new JwtOptions { SigningKey = new string('k', length) }.IsUsable());
    }

    [Fact]
    public void The_api_refuses_to_start_with_a_weak_signing_key()
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseSetting("Jwt:SigningKey", "too-short"));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", exception.Message);
    }

    [GeneratedRegex(@"\{[^}:]+(:(?<type>[a-z]+))?\}")]
    private static partial Regex Placeholder();
}
