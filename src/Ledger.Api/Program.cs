using System.Text.Json.Serialization;
using Ledger.Api;
using Ledger.Api.Auth;
using Ledger.Api.Data;
using Ledger.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LedgerDbContext>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<IConfiguration>().GetConnectionString("Ledger")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<LedgerRuleExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.AddOptions<JwtOptions>()
    .BindConfiguration(JwtOptions.Section)
    .Validate(jwt => jwt.IsUsable(), $"Jwt:SigningKey must be at least {JwtOptions.MinKeyBytes} bytes, with an issuer, an audience and a positive lifetime.")
    .ValidateOnStart();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
    {
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = jwt.Value.ValidationParameters();
    });
builder.Services.AddAuthorizationBuilder().AddLedgerPolicies();

builder.Services.AddSingleton<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddScoped<UserDirectory>();

builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapAuthEndpoints();
app.MapAccountEndpoints();
app.MapJournalEndpoints();
app.MapPeriodEndpoints();
app.MapCustomerEndpoints();
app.MapInvoiceEndpoints();
app.MapPaymentEndpoints();
app.MapReportEndpoints();

if (app.Configuration.GetValue<bool>("Ledger:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
    await FirstAdmin.EnsureAsync(scope.ServiceProvider, app.Configuration);
}

await app.RunAsync();
