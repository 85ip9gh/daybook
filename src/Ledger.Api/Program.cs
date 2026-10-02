using System.Text.Json.Serialization;
using Ledger.Api;
using Ledger.Api.Data;
using Ledger.Api.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LedgerDbContext>((services, options) =>
    options.UseSqlServer(services.GetRequiredService<IConfiguration>().GetConnectionString("Ledger")));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<LedgerRuleExceptionHandler>();
builder.Services.AddOpenApi();

builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.RespectNullableAnnotations = true;
    options.SerializerOptions.RespectRequiredConstructorParameters = true;
});

var app = builder.Build();

app.UseExceptionHandler();
app.MapOpenApi();
app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapAccountEndpoints();
app.MapJournalEndpoints();
app.MapReportEndpoints();

if (app.Configuration.GetValue<bool>("Ledger:MigrateOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<LedgerDbContext>().Database.MigrateAsync();
}

await app.RunAsync();
