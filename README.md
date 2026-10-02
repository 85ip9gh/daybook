# Ledger

A double-entry general ledger exposed as an HTTP API. C# on .NET 10, ASP.NET Core minimal APIs, EF Core, SQL Server.

The interesting part is not the endpoints. It is that the rules of bookkeeping hold when requests are retried, raced, or malformed, and that each of those claims has a test that fails when the code protecting it is removed.

## What it guarantees

- **Every entry balances.** Debits equal credits or the entry is refused with `422` and nothing is written.
- **The journal is append-only.** There is no update or delete endpoint. A mistake is corrected by a reversal, which is a new entry that mirrors every line and points at the original.
- **A retried post does not post twice.** `POST /journal-entries` requires an `Idempotency-Key` header. A retry with the same key and the same body returns the original entry with `200` and `Idempotent-Replay: true`. The same key with a different body is `409`.
- **That holds under concurrency.** The key is a primary key and it is written in the same transaction as the entry. Concurrent requests with one key race on the constraint, one commits, and the rest read the winner back.
- **An entry is reversed at most once.** A filtered unique index on `ReversesEntryId` decides concurrent reversals, not a check in application code.
- **Money is `decimal(19,4)`.** Amounts are positive, carry at most four decimal places, and are never floating point. The database repeats the positive-amount rule as a check constraint.
- **Balances are derived.** No balance column is stored, so there is nothing to drift out of step with the journal. A balance is a `SUM` over lines up to a date.

## Layout

```
src/Ledger.Domain   entities and rules, no dependencies
src/Ledger.Api      endpoints, EF Core mapping, migrations
tests/Ledger.Domain.Tests   38 tests, no database, no container
tests/Ledger.Api.Tests      17 tests against SQL Server 2022 in a container
```

The domain project references nothing outside the base class library. Every bookkeeping rule lives there and is tested without a database.

## Endpoints

| Method | Path | Purpose |
|---|---|---|
| POST | `/accounts` | Open an account |
| GET | `/accounts` | List accounts |
| POST | `/accounts/{id}/deactivate` | Stop new postings to an account |
| GET | `/accounts/{id}/balance?asOf=` | Balance on the account's normal side |
| POST | `/journal-entries` | Post an entry, idempotent by key |
| GET | `/journal-entries/{id}` | Read an entry |
| POST | `/journal-entries/{id}/reversal` | Reverse an entry |
| GET | `/trial-balance?asOf=` | Trial balance |
| GET | `/openapi/v1.json` | OpenAPI document |

Rule violations return RFC 9457 problem details with a stable `code` field such as `entry_unbalanced` or `currency_mismatch`.

## Running the tests

Docker has to be running for the API tests, which start SQL Server through Testcontainers.

```bash
dotnet test
```

The domain tests alone need nothing:

```bash
dotnet test tests/Ledger.Domain.Tests
```

## Running the API

```bash
docker run -d --name ledger-sql -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD="$MSSQL_SA_PASSWORD" -p 1433:1433 mcr.microsoft.com/mssql/server:2022-latest
```

```bash
ConnectionStrings__Ledger="Server=localhost,1433;Database=Ledger;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True" Ledger__MigrateOnStartup=true dotnet run --project src/Ledger.Api
```

## How the claims were checked

- The two concurrency tests send 24 simultaneous posts with one key and 16 simultaneous reversals of one entry. With the unique-violation handling disabled, both fail. With it in place, exactly one request creates and the rest replay or conflict.
- One test bypasses the API and inserts a negative amount with raw SQL, to show the check constraint refuses it independently of the application.
- One domain test posts 500 random entries with random reversals from three fixed seeds and asserts the trial balance still balances.
- CI fails if the EF Core model has changes that no migration covers.

## Limitations

- No authentication or authorisation. Anyone who can reach the API can post.
- An entry has one currency and every account on it must hold that currency. There is no conversion.
- No accounting periods, so nothing stops a post dated into a period that should be closed.
- Append-only is enforced by the API surface, not by database permissions or triggers.
- A refused request does not consume its idempotency key, so only successful posts are replayed.
- Not deployed anywhere. The evidence is this repository and its test run.
