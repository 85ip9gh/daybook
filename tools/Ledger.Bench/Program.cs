using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Ledger.Api.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

var entryCount = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 1_000_000;
const int AccountCount = 200;
const int Warmups = 2;
const int Runs = 9;
const int BatchSize = 50_000;

var firstDay = new DateOnly(2024, 1, 1);
var lastDay = new DateOnly(2025, 12, 31);
var closed = new DateOnly(2025, 11, 30);
var span = lastDay.DayNumber - firstDay.DayNumber + 1;

await using var container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();
await container.StartAsync();
var connectionString = new SqlConnectionStringBuilder(container.GetConnectionString()) { InitialCatalog = "LedgerBench" }.ConnectionString;

await using (var db = new LedgerDbContext(new DbContextOptionsBuilder<LedgerDbContext>().UseSqlServer(connectionString).Options))
{
    await db.Database.MigrateAsync();
}

await using var connection = new SqlConnection(connectionString);
await connection.OpenAsync();

var random = new Random(20261003);
var accountIds = Enumerable.Range(0, AccountCount).Select(_ => Guid.NewGuid()).ToArray();

var accounts = Table(("Id", typeof(Guid)), ("Code", typeof(string)), ("Name", typeof(string)), ("Type", typeof(byte)), ("Currency", typeof(string)), ("IsActive", typeof(bool)));
for (var index = 0; index < AccountCount; index++)
{
    accounts.Rows.Add(accountIds[index], $"B{index:D5}", $"Bench account {index}", (byte)(index % 5 + 1), "CAD", true);
}

await BulkAsync("Accounts", accounts);

var seeding = Stopwatch.StartNew();
for (var done = 0; done < entryCount; done += BatchSize)
{
    var entries = Table(
        ("Id", typeof(Guid)), ("EntryDate", typeof(DateTime)), ("Description", typeof(string)), ("Currency", typeof(string)),
        ("RecordedAt", typeof(DateTimeOffset)), ("PostedBy", typeof(string)));
    var lines = Table(
        ("JournalEntryId", typeof(Guid)), ("LineNumber", typeof(int)), ("AccountId", typeof(Guid)), ("Side", typeof(byte)),
        ("Amount", typeof(decimal)), ("EntryDate", typeof(DateTime)));

    for (var index = 0; index < Math.Min(BatchSize, entryCount - done); index++)
    {
        var id = Guid.NewGuid();
        var date = firstDay.AddDays(random.Next(span)).ToDateTime(TimeOnly.MinValue);
        var amount = random.Next(1, 500_000) / 100m;
        var debit = random.Next(AccountCount);
        var credit = (debit + 1 + random.Next(AccountCount - 1)) % AccountCount;

        entries.Rows.Add(id, date, "Bench entry", "CAD", DateTimeOffset.UtcNow, "bench");
        lines.Rows.Add(id, 1, accountIds[debit], (byte)1, amount, date);
        lines.Rows.Add(id, 2, accountIds[credit], (byte)2, amount, date);
    }

    await BulkAsync("JournalEntries", entries);
    await BulkAsync("JournalLines", lines);
}

seeding.Stop();

await ExecuteAsync("INSERT INTO PeriodCloses (PeriodEnd, ClosedAt, ClosedBy) VALUES (@closed, SYSDATETIMEOFFSET(), 'bench')", ("@closed", closed));
await ExecuteAsync(
    """
    INSERT INTO PeriodBalances (PeriodEnd, AccountId, Debits, Credits)
    SELECT @closed, AccountId, SUM(CASE WHEN Side = 1 THEN Amount ELSE 0 END), SUM(CASE WHEN Side = 2 THEN Amount ELSE 0 END)
    FROM JournalLines WHERE EntryDate <= @closed GROUP BY AccountId
    """,
    ("@closed", closed));
await ExecuteAsync("UPDATE LedgerState SET ClosedThrough = @closed WHERE Id = 1", ("@closed", closed));

const string Sums = "SUM(CASE WHEN l.Side = 1 THEN l.Amount ELSE 0 END) AS Debits, SUM(CASE WHEN l.Side = 2 THEN l.Amount ELSE 0 END) AS Credits";
const string Joined = $"SELECT l.AccountId, {Sums} FROM JournalLines AS l INNER JOIN JournalEntries AS e ON l.JournalEntryId = e.Id WHERE e.EntryDate <= @asOf";
const string Direct = $"SELECT l.AccountId, {Sums} FROM JournalLines AS l WHERE l.EntryDate <= @asOf";
const string Since = $"SELECT l.AccountId, {Sums} FROM JournalLines AS l WHERE l.EntryDate > @closed AND l.EntryDate <= @asOf";
const string Snapshot = "SELECT b.AccountId, b.Debits, b.Credits FROM PeriodBalances AS b WHERE b.PeriodEnd = @closed";

var oneAccount = accountIds[AccountCount / 2];
var results = new List<Measurement>();

await ExecuteAsync("DROP INDEX IX_JournalLines_AccountId_EntryDate ON JournalLines");
await ExecuteAsync("CREATE INDEX IX_JournalLines_AccountId ON JournalLines (AccountId)");
await ExecuteAsync("UPDATE STATISTICS JournalLines WITH FULLSCAN; UPDATE STATISTICS JournalEntries WITH FULLSCAN");
results.Add(await MeasureAsync("One account", "1. Join for the date, index on AccountId", [Joined + " AND l.AccountId = @account GROUP BY l.AccountId"], oneAccount));
results.Add(await MeasureAsync("All accounts", "1. Join for the date, index on AccountId", [Joined + " GROUP BY l.AccountId"], null));

await ExecuteAsync("DROP INDEX IX_JournalLines_AccountId ON JournalLines");
await ExecuteAsync("CREATE INDEX IX_JournalLines_AccountId_EntryDate ON JournalLines (AccountId, EntryDate) INCLUDE (Side, Amount)");
await ExecuteAsync("UPDATE STATISTICS JournalLines WITH FULLSCAN");
results.Add(await MeasureAsync("One account", "2. Date on the line, covering index", [Direct + " AND l.AccountId = @account GROUP BY l.AccountId"], oneAccount));
results.Add(await MeasureAsync("All accounts", "2. Date on the line, covering index", [Direct + " GROUP BY l.AccountId"], null));
results.Add(await MeasureAsync(
    "One account",
    "3. Close snapshot plus lines since",
    [Snapshot + " AND b.AccountId = @account", Since + " AND l.AccountId = @account GROUP BY l.AccountId"],
    oneAccount));
results.Add(await MeasureAsync("All accounts", "3. Close snapshot plus lines since", [Snapshot, Since + " GROUP BY l.AccountId"], null));

await ExecuteAsync("CREATE INDEX IX_JournalLines_EntryDate ON JournalLines (EntryDate) INCLUDE (AccountId, Side, Amount)");
await ExecuteAsync("UPDATE STATISTICS JournalLines WITH FULLSCAN");
results.Add(await MeasureAsync(
    "One account",
    "4. Shape 3 with a second index leading on EntryDate",
    [Snapshot + " AND b.AccountId = @account", Since + " AND l.AccountId = @account GROUP BY l.AccountId"],
    oneAccount));
results.Add(await MeasureAsync("All accounts", "4. Shape 3 with a second index leading on EntryDate", [Snapshot, Since + " GROUP BY l.AccountId"], null));

var indexPages = new List<(string Name, long Pages)>();
await using (var sizes = Command(
    """
    SELECT i.name, SUM(s.used_page_count)
    FROM sys.dm_db_partition_stats AS s
    JOIN sys.indexes AS i ON i.object_id = s.object_id AND i.index_id = s.index_id
    WHERE s.object_id = OBJECT_ID('JournalLines')
    GROUP BY i.name
    ORDER BY i.name
    """))
await using (var reader = await sizes.ExecuteReaderAsync())
{
    while (await reader.ReadAsync())
    {
        indexPages.Add((reader.GetString(0), reader.GetInt64(1)));
    }
}

var version = ((string)(await ScalarAsync("SELECT @@VERSION"))!).Split('\n')[0].Trim();
var lineCount = Convert.ToInt64(await ScalarAsync("SELECT COUNT_BIG(*) FROM JournalLines"), CultureInfo.InvariantCulture);

Console.WriteLine($"Entries: {entryCount:N0}. Lines: {lineCount:N0}. Accounts: {AccountCount}. Dates: {firstDay:yyyy-MM-dd} to {lastDay:yyyy-MM-dd}. Closed through {closed:yyyy-MM-dd}.");
Console.WriteLine($"Seeded in {seeding.Elapsed.TotalSeconds:F0} s. {version}");
Console.WriteLine($"Host: {RuntimeInformation.OSDescription}, {Environment.ProcessorCount} logical processors. Median of {Runs} runs after {Warmups} warm-ups.");
Console.WriteLine();
Console.WriteLine("| Query | Shape | Median ms | Logical reads | Rows |");
Console.WriteLine("|---|---|---:|---:|---:|");
foreach (var result in results.OrderBy(result => result.Query).ThenBy(result => result.Shape))
{
    Console.WriteLine($"| {result.Query} | {result.Shape} | {result.MedianMs:F1} | {result.LogicalReads:N0} | {result.Rows} |");
}

foreach (var group in results.GroupBy(result => result.Query))
{
    var agree = group.Select(result => result.Checksum).Distinct().Count() == 1;
    Console.WriteLine();
    Console.WriteLine($"{group.Key}: every shape returns the same totals: {agree}");
    if (!agree)
    {
        Environment.ExitCode = 1;
    }
}

Console.WriteLine();
Console.WriteLine("| Index on JournalLines | 8 KB pages |");
Console.WriteLine("|---|---:|");
foreach (var (name, pages) in indexPages)
{
    Console.WriteLine($"| {name} | {pages:N0} |");
}

static DataTable Table(params (string Name, Type Type)[] columns)
{
    var table = new DataTable();
    foreach (var (name, type) in columns)
    {
        table.Columns.Add(name, type);
    }

    return table;
}

async Task BulkAsync(string destination, DataTable table)
{
    using var bulk = new SqlBulkCopy(connection) { DestinationTableName = destination, BulkCopyTimeout = 0, BatchSize = table.Rows.Count };
    foreach (DataColumn column in table.Columns)
    {
        bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
    }

    await bulk.WriteToServerAsync(table);
}

SqlCommand Command(string sql, params (string Name, object Value)[] parameters)
{
    var command = new SqlCommand(sql, connection) { CommandTimeout = 0 };
    foreach (var (name, value) in parameters)
    {
        command.Parameters.AddWithValue(name, value is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : value);
    }

    return command;
}

async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
{
    await using var command = Command(sql, parameters);
    await command.ExecuteNonQueryAsync();
}

async Task<long> SessionReadsAsync() =>
    Convert.ToInt64(await ScalarAsync("SELECT logical_reads FROM sys.dm_exec_sessions WHERE session_id = @@SPID"), CultureInfo.InvariantCulture);

async Task<object?> ScalarAsync(string sql)
{
    await using var command = Command(sql);
    return await command.ExecuteScalarAsync();
}

async Task<(Dictionary<Guid, (decimal Debits, decimal Credits)> Totals, long Reads)> RunAsync(string[] statements, Guid? account, bool countReads)
{
    var totals = new Dictionary<Guid, (decimal Debits, decimal Credits)>();
    var readsBefore = countReads ? await SessionReadsAsync() : 0;

    foreach (var statement in statements)
    {
        await using var command = Command(statement, ("@asOf", lastDay), ("@closed", closed), ("@account", account ?? Guid.Empty));
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var id = reader.GetGuid(0);
            var existing = totals.GetValueOrDefault(id);
            totals[id] = (existing.Debits + reader.GetDecimal(1), existing.Credits + reader.GetDecimal(2));
        }
    }

    return (totals, countReads ? await SessionReadsAsync() - readsBefore : 0);
}

async Task<Measurement> MeasureAsync(string query, string shape, string[] statements, Guid? account)
{
    for (var warmup = 0; warmup < Warmups; warmup++)
    {
        await RunAsync(statements, account, false);
    }

    var timings = new List<double>();
    Dictionary<Guid, (decimal Debits, decimal Credits)> totals = [];
    for (var run = 0; run < Runs; run++)
    {
        var watch = Stopwatch.StartNew();
        (totals, _) = await RunAsync(statements, account, false);
        watch.Stop();
        timings.Add(watch.Elapsed.TotalMilliseconds);
    }

    var (_, reads) = await RunAsync(statements, account, true);
    timings.Sort();

    var checksum = string.Join(";", totals.OrderBy(total => total.Key).Select(total => $"{total.Key:N}:{total.Value.Debits}:{total.Value.Credits}"));
    return new Measurement(query, shape, timings[Runs / 2], reads, totals.Count, checksum);
}

internal sealed record Measurement(string Query, string Shape, double MedianMs, long LogicalReads, int Rows, string Checksum);
