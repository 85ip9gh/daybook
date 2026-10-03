# Balance query tuning

Measured 2026-10-03 with `tools/Ledger.Bench`. Every number below came out of that tool on that day. Nothing here is estimated.

## What was slow

A balance is a `SUM` over journal lines up to a date. The date lived on the entry, not the line, so every balance query joined two tables, and the only index on lines was on `AccountId` alone. Asking for one account's balance read almost as many pages as asking for all of them.

## What changed

1. **The entry date is copied onto each line**, with an index on `(AccountId, EntryDate)` that includes `Side` and `Amount`. A balance for one account becomes a single range seek on one index, with no join and no lookups. The copy cannot drift: entries are never updated, and a foreign key on `(JournalEntryId, EntryDate)` makes the database refuse a line whose date differs from its entry.
2. **A balance starts from the last period close.** Closing a period already stores every account's totals under a lock that no post can interleave with, so those totals are final. A balance is now that snapshot plus the lines dated after it.

## Results

1,000,000 entries, 2,000,000 lines, 200 accounts, dates spread evenly over 2024 and 2025, books closed through 2025-11-30, balances asked as of 2025-12-31. SQL Server 2022 (16.0.4295.3) in a Docker Desktop container on a Windows 11 desktop with 28 logical processors. Median of 9 runs after 2 warm-ups.

| Query | Shape | Median ms | Logical reads |
|---|---|---:|---:|
| One account | 1. Join for the date, index on `AccountId` | 41.7 | 38,810 |
| One account | 2. Date on the line, covering index | 2.3 | 76 |
| One account | 3. Close snapshot plus lines since | 1.3 | 11 |
| All accounts | 1. Join for the date, index on `AccountId` | 66.8 | 38,261 |
| All accounts | 2. Date on the line, covering index | 48.0 | 14,113 |
| All accounts | 3. Close snapshot plus lines since | 19.5 | 14,117 |

Shape 3 is what the code runs now. The tool checks that all shapes return identical totals for every account and exits non-zero if they do not.

The tool was run three times. Logical reads moved by well under one percent between runs, because each run seeds new random identifiers. Times for shapes 2 and 3 stayed within a few tenths of a millisecond for one account and within one millisecond for all accounts. The one-account baseline was the noisy figure: 39.9, 70.1 and 41.7 ms across the three runs.

## An index that was measured and left out

For all accounts, shape 3 reads as many pages as shape 2. The index leads on `AccountId`, so a date range across every account still scans all of it. A second index leading on `EntryDate` fixes that:

| Query | Shape | Median ms | Logical reads |
|---|---|---:|---:|
| All accounts | 3. Close snapshot plus lines since | 19.5 | 14,117 |
| All accounts | 4. Shape 3 with a second index leading on `EntryDate` | 24.1 | 586 |

Reads fell by a factor of 24 and the query got no faster. In both runs that measured it, it was a few milliseconds slower. With every page already in memory, scanning 14,000 pages costs less than the plan change. The index would also be 13,712 pages, about 107 MB at this size, maintained on every insert into an append-only table. It is not in the schema. It would earn its place on storage where reads are the cost, and this benchmark cannot show that.

## An index that was found and removed

The first version of this change modelled the `(JournalEntryId, EntryDate)` foreign key in EF Core. EF adds an index for every foreign key it knows about, and the benchmark's index listing showed it: 17,975 pages, about 140 MB, larger than the covering index and used by nothing, since the primary key already starts with `JournalEntryId`. The constraint is now created with SQL in the migration, where no index comes with it.

| Index on `JournalLines` | 8 KB pages |
|---|---:|
| `PK_JournalLines` | 30,075 |
| `IX_JournalLines_AccountId_EntryDate` | 13,747 |

## What this does not show

- The cache was warm and nothing else was using the server. Cold reads and concurrent posting were not measured.
- The cost of the change on writes was not measured: one wider index and one extra foreign key check per line.
- The data is synthetic and uniform. Real ledgers have a few very busy accounts and many quiet ones.
- The queries are hand-written to the same shape as the LINQ in the API. They were not captured from EF Core.
- Shape 1 ran against today's table, which carries the extra three-byte date column the old schema did not have.
- The snapshot only helps for dates after a close. With no close, shape 3 is shape 2.

## Running it

Docker has to be running. The argument is the number of entries.

```bash
dotnet run --project tools/Ledger.Bench -c Release -- 1000000
```
