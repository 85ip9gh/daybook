# Ledger

A double-entry general ledger with accounts receivable on top of it, exposed as an HTTP API. C# on .NET 10, ASP.NET Core minimal APIs, EF Core, SQL Server.

The interesting part is not the endpoints. It is that the rules of bookkeeping hold when requests are retried, raced, or malformed, and that each of those claims has a test that fails when the code protecting it is removed.

## What it guarantees

- **Every entry balances.** Debits equal credits or the entry is refused with `422` and nothing is written.
- **The journal is append-only.** There is no update or delete endpoint. A mistake is corrected by a reversal, which is a new entry that mirrors every line and points at the original.
- **A retried post does not post twice.** `POST /journal-entries` requires an `Idempotency-Key` header. A retry with the same key and the same body returns the original entry with `200` and `Idempotent-Replay: true`. The same key with a different body is `409`.
- **That holds under concurrency.** The key is a primary key and it is written in the same transaction as the entry. Concurrent requests with one key race on the constraint, one commits, and the rest read the winner back.
- **An entry is reversed at most once.** A filtered unique index on `ReversesEntryId` decides concurrent reversals, not a check in application code.
- **Money is `decimal(19,4)`.** Amounts are positive, carry at most four decimal places, and are never floating point. The database repeats the positive-amount rule as a check constraint.
- **Live balances are derived.** No running balance is stored, so there is nothing to drift out of step with the journal. A balance is a `SUM` over lines up to a date.
- **A closed period stays closed.** Closing a month refuses every later post or reversal dated on or before its last day, and records each account's totals as of that day. The books only close forward.
- **A close and a post cannot interleave.** Every post reads the closed-through date with `HOLDLOCK` inside its transaction, which holds a shared lock until commit. The close updates that row, so it waits for every post already in flight, and every post that starts later sees the new date. The snapshot therefore contains exactly the entries dated in the period. The hint is explicit so the lock is taken even where read committed snapshot isolation is on, as it is by default on Azure SQL.

### Receivables

Nobody types debits and credits for a sale. An invoice or a payment is the business record, and the journal entry is written in the same transaction, so one cannot exist without the other.

- **Issuing an invoice posts it.** A draft touches nothing. Issuing debits the receivable account and credits revenue and tax, numbers the invoice, and commits all of it together.
- **Invoice numbers have no gaps.** The number comes from a counter row updated inside the issuing transaction, not from a database sequence, so a failed issue rolls its number back. The cost is that issues are serialised on that row.
- **An invoice is issued once and cannot be overpaid.** Invoices carry a `rowversion` concurrency token. Two requests that read the same invoice cannot both write it: the second fails with `409 invoice_changed` and the client retries. A check constraint repeats `AmountPaid <= Total` in the database.
- **Payments are allocated.** One payment can settle several invoices, an invoice can be paid in parts, and allocations must add up to the payment. Payments are idempotent by key like journal posts.
- **What customers owe equals the ledger.** The sum of outstanding invoices equals the balance of the receivable control account, and a test checks it after invoices, a split payment and a void.
- **Rounding is commercial.** Line amounts and tax round half away from zero to two places. `Math.Round` defaults to half-to-even, which is not what an invoice should do.

## Layout

```
src/Ledger.Domain   entities and rules, no dependencies
src/Ledger.Api      endpoints, EF Core mapping, migrations
tests/Ledger.Domain.Tests   71 tests, no database, no container
tests/Ledger.Api.Tests      45 tests against SQL Server 2022 in a container
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
| GET | `/periods` | Closed-through date and recorded closes |
| POST | `/periods/{year}/{month}/close` | Close the books through that month |
| GET | `/periods/{year}/{month}/trial-balance` | Trial balance frozen at that close |
| POST | `/customers` | Register a customer |
| GET | `/customers` | List customers |
| GET | `/customers/{id}/receivables` | Open invoices and the total owed |
| POST | `/invoices` | Draft an invoice |
| GET | `/invoices/{id}` | Read an invoice |
| POST | `/invoices/{id}/issue` | Number it and post it |
| POST | `/invoices/{id}/void` | Void an unpaid invoice by reversing its entry |
| POST | `/payments` | Receive a payment and allocate it, idempotent by key |
| GET | `/payments/{id}` | Read a payment |
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
- The period race test runs six rounds of 40 posts dated on a month's last day while that month is being closed. It asserts every post was either created or refused with `period_closed`, and that the stored snapshot equals the journal. With the `HOLDLOCK` hint removed it failed five runs out of five, with entries in the closed period that the snapshot had missed.
- Twelve simultaneous payments of 30 are sent against one invoice of 100, and twelve simultaneous issues against one draft. With the concurrency token disabled, all twelve payments were accepted and the draft was issued twelve times, three runs out of three. With it in place, at most three payments land and the draft is issued once.
- Twenty drafts issued at once must come back with twenty consecutive numbers, and an issue that fails after taking a number must leave the next invoice with the very next one.
- One test bypasses the API and inserts a negative amount with raw SQL, to show the check constraint refuses it independently of the application.
- One domain test posts 500 random entries with random reversals from three fixed seeds and asserts the trial balance still balances.
- CI fails if the EF Core model has changes that no migration covers.

## Limitations

- No authentication or authorisation. Anyone who can reach the API can post.
- An entry has one currency and every account on it must hold that currency. There is no conversion.
- A payment must be allocated in full. There is no unapplied cash, no credit notes, no refunds, and no accounts payable side.
- A payment that loses a concurrency race is refused, not retried by the server.
- A close cannot be reopened, and there are no year-end closing entries moving revenue and expense into retained earnings.
- Every post takes a shared lock on one row. Posts do not block each other, but a close waits for all of them and they wait for it.
- Append-only is enforced by the API surface, not by database permissions or triggers.
- A refused request does not consume its idempotency key, so only successful posts are replayed.
- Not deployed anywhere. The evidence is this repository and its test run.
