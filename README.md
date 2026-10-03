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
- **Balances are derived, never edited.** No running balance is stored. A balance is the totals frozen at the last period close plus a `SUM` over the lines dated after it, and a test checks that against the raw journal on both sides of a close.
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

### Who may do what

- **Every route needs a token except three.** A fallback policy requires an authenticated user, so a route added without a policy is closed, not open. `GET /healthz`, the OpenAPI document and `POST /auth/token` are the only public ones, and a test walks every mapped route to prove it.
- **Tokens are JWTs signed with HS256.** Issuer, audience, signature, algorithm and expiry are all validated, with 30 seconds of clock skew and a 30 minute lifetime. The API refuses to start with a signing key under 256 bits.
- **Passwords are stored as PBKDF2 hashes** through ASP.NET Core's `PasswordHasher`. A wrong password and an unknown user get the same answer, and the unknown-user path still runs a hash so the two take similar time.
- **Roles are narrow and separate.** Viewer reads. Clerk registers customers, drafts invoices and receives payments. Approver issues and voids invoices and posts and reverses journal entries. Controller opens accounts and closes periods. Admin manages users and cannot read the books.
- **Holding two roles is not enough to approve your own work.** Whoever drafted an invoice cannot issue it. That rule lives in the domain, not in a route policy, and answers `403 segregation_of_duties`.
- **Every record names its author.** Journal entries, invoices, payments and period closes store the user who made them, and the domain refuses to post an entry attributed to nobody.

### Bank reconciliation

A bank statement is uploaded as CSV, accepted at once with `202`, and parsed and matched by a background worker.

- **The upload only stores the file.** Parsing and matching happen in a `BackgroundService`, so a large statement never holds a request open. The client polls the statement for its status.
- **Statements are claimed from a queue table.** A worker takes the next one with a single `UPDATE TOP (1) ... OUTPUT` using `READPAST` and `UPDLOCK`, so two workers never claim the same statement and neither waits behind the other.
- **An abandoned statement is picked up again.** A claim is a lease. If a worker dies mid-statement, the claim goes stale and another worker takes it.
- **Re-importing is safe at two levels.** The same file for the same account returns the statement already stored. A different file that overlaps an earlier one adds only the lines whose bank reference has not been seen, enforced by a unique index on account and reference.
- **A bad file fails whole.** Every bad line is reported with its line number and no line is stored. The CSV reader is written here and handles quoted fields, escaped quotes and line breaks inside quotes.
- **Matching is exact and deterministic.** A bank line matches a ledger line on the same account with the same signed amount within three days. Within each amount, both sides are walked in date order, which finds the most pairs a date window allows where taking the nearest candidate first can strand one.
- **A ledger line reconciles one bank line.** A filtered unique index decides it. A worker that loses that race releases its statement and tries again, up to five times.
- **What the matcher cannot place waits for a person.** An approver matches or unmatches by hand. Amounts must be equal, and bank lines carry a `rowversion` token.
- **The report shows both sides.** Bank lines not in the books, ledger lines not at the bank, and the two totals that must agree once they are removed.

### Reports and query tuning

- **Income statement, balance sheet, aged receivables and an account statement.** The balance sheet reports whether it balances. Aged receivables answer for any past date: payments and voids dated after it are ignored. The account statement computes its running balance in SQL with a window function.
- **The balance query was measured and tuned.** On 2,000,000 journal lines, one account's balance went from 41.7 ms and 38,810 logical reads to 1.3 ms and 11. The trial balance went from 66.8 ms to 19.5 ms. The method, the full table, an index that was measured and rejected, and what the numbers do not show are in [docs/tuning.md](docs/tuning.md).
- **The entry date is copied onto every line** so a balance needs one index and no join. A foreign key on the pair makes the database refuse a line whose date differs from its entry, so the copy cannot drift.

## Layout

```
src/Ledger.Domain   entities and rules, no dependencies
src/Ledger.Api      endpoints, EF Core mapping, migrations
tests/Ledger.Domain.Tests   113 tests, no database, no container
tests/Ledger.Api.Tests      109 tests against SQL Server 2022 in a container
tools/Ledger.Bench  seeds a large journal and measures the balance queries
tools/seed-demo.mjs fills an empty database with a small set of books and demo users
docs/tuning.md      what was measured, on what, and what it does not show
docs/sample-statement.csv  a bank statement that fits the demo data, to try the import
web/                Angular front end, 69 tests, described in web/README.md
deploy/compose.yaml SQL Server Express, the API and nginx, one published port
```

The domain project references nothing outside the base class library. Every bookkeeping rule lives there and is tested without a database.

## Endpoints

Every `GET` below needs Viewer, Clerk, Approver or Controller unless the table says otherwise.

| Method | Path | Role | Purpose |
|---|---|---|---|
| POST | `/auth/token` | public | Exchange a user name and password for a token |
| POST | `/users` | Admin | Create a user |
| GET | `/users` | Admin | List users |
| PUT | `/users/{id}/roles` | Admin | Replace a user's roles |
| POST | `/users/{id}/deactivate` | Admin | Stop a user signing in |
| POST | `/accounts` | Controller | Open an account |
| GET | `/accounts` | read | List accounts |
| POST | `/accounts/{id}/deactivate` | Controller | Stop new postings to an account |
| GET | `/accounts/{id}/balance?asOf=` | read | Balance on the account's normal side |
| POST | `/journal-entries` | Approver | Post an entry, idempotent by key |
| GET | `/journal-entries/{id}` | read | Read an entry |
| POST | `/journal-entries/{id}/reversal` | Approver | Reverse an entry |
| GET | `/trial-balance?asOf=` | read | Trial balance |
| GET | `/reports/income-statement?from=&to=` | read | Revenue, expenses and net income for a range |
| GET | `/reports/balance-sheet?asOf=` | read | Assets, liabilities, equity and current earnings |
| GET | `/reports/aged-receivables?asOf=` | read | What each customer owed on a date, by how overdue |
| GET | `/accounts/{id}/statement?from=&to=` | read | Lines with a running balance |
| GET | `/periods` | read | Closed-through date and recorded closes |
| POST | `/periods/{year}/{month}/close` | Controller | Close the books through that month |
| GET | `/periods/{year}/{month}/trial-balance` | read | Trial balance frozen at that close |
| POST | `/customers` | Clerk | Register a customer |
| GET | `/customers` | read | List customers |
| GET | `/customers/{id}/receivables` | read | Open invoices and the total owed |
| POST | `/invoices` | Clerk | Draft an invoice |
| GET | `/invoices?status=&customerId=` | read | List invoices, drafts first |
| GET | `/invoices/{id}` | read | Read an invoice |
| POST | `/invoices/{id}/issue` | Approver | Number it and post it |
| POST | `/invoices/{id}/void` | Approver | Void an unpaid invoice by reversing its entry |
| POST | `/payments` | Clerk | Receive a payment and allocate it, idempotent by key |
| GET | `/payments/{id}` | read | Read a payment |
| GET | `/openapi/v1.json` | public | OpenAPI document |
| POST | `/bank-accounts/{accountId}/statements` | Clerk | Upload a CSV statement, answered `202` |
| GET | `/bank-accounts/{accountId}/statements` | read | The twenty most recent imports |
| GET | `/bank-statements/{id}` | read | Status, counts and any error |
| GET | `/bank-accounts/{accountId}/reconciliation?asOf=` | read | Both unreconciled sides and their totals |
| POST | `/bank-lines/{id}/match` | Approver | Match a bank line to a journal line by hand |
| POST | `/bank-lines/{id}/unmatch` | Approver | Undo a match |
| GET | `/healthz` | public | Liveness |

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
ConnectionStrings__Ledger="Server=localhost,1433;Database=Ledger;User Id=sa;Password=$MSSQL_SA_PASSWORD;TrustServerCertificate=True" Jwt__SigningKey="$LEDGER_JWT_KEY" Ledger__FirstAdmin__UserName=admin Ledger__FirstAdmin__Password="$LEDGER_ADMIN_PASSWORD" Ledger__MigrateOnStartup=true dotnet run --project src/Ledger.Api
```

`Jwt__SigningKey` must be at least 32 bytes. The first admin is created only when the users table is empty. No secret has a default and none is committed.

For day-to-day development the same three secrets can live in .NET user secrets, which sit outside the repository and are read automatically in the Development environment:

```bash
dotnet user-secrets set "Jwt:SigningKey" "$LEDGER_JWT_KEY" --project src/Ledger.Api
```

The front end is in `web/` and its README covers running it against this API.

## Running the whole stack

`deploy/compose.yaml` runs three containers: SQL Server 2022 Express, the API, and nginx serving the built front end and proxying `/api/` to the API. Only nginx publishes a port. The database and the API are reachable only on the compose network.

Put the four values in `deploy/.env`, which git ignores:

```
MSSQL_SA_PASSWORD=...
LEDGER_JWT_KEY=...
LEDGER_ADMIN_PASSWORD=...
LEDGER_BIND=127.0.0.1:8080
```

```bash
docker compose --file deploy/compose.yaml up -d --build
```

Compose refuses to start if any of the four is missing, so an empty `LEDGER_BIND` cannot publish on every interface. The API applies migrations and creates the first admin on start. `tools/seed-demo.mjs` with `LEDGER_API=http://127.0.0.1:8080/api` fills it with demo books.

- **Express, not Developer.** Developer is licensed for development and test only. Express is free for production use and its limits (1,410 MB buffer pool, 10 GB per database) are far above what this needs.
- **Memory.** SQL Server needs 2 GB to start on Linux, so its container is capped at exactly that, with `MSSQL_MEMORY_LIMIT_MB=1536` inside it. Measured idle after a restart with the demo books loaded: SQL Server 580 MiB, the API 77 MiB, nginx 10 MiB.
- **The API image is chiseled**: no shell, no package manager, a non-root user. It is the `-extra` variant because SqlClient needs ICU, which the plain chiseled image leaves out.
- **Both app containers run with a read-only root filesystem.**
- **nginx sends a strict Content Security Policy.** Scripts load only from the site itself, so the production build turns off Angular's critical CSS inlining, which loads the stylesheet through an inline `onload` handler the policy blocks. Hashed assets are cached for a year, `index.html` and `config.json` are not cached.
- **Uploads stop at 2 MB in nginx**, above the API's own limit of one million characters per statement.
- **The API connects as `sa`.** A login limited to the one database would be tighter. It is not done yet.

## How the claims were checked

- The two concurrency tests send 24 simultaneous posts with one key and 16 simultaneous reversals of one entry. With the unique-violation handling disabled, both fail. With it in place, exactly one request creates and the rest replay or conflict.
- The period race test runs six rounds of 40 posts dated on a month's last day while that month is being closed. It asserts every post was either created or refused with `period_closed`, and that the stored snapshot equals the journal. With the `HOLDLOCK` hint removed it failed five runs out of five, with entries in the closed period that the snapshot had missed.
- Twelve simultaneous payments of 30 are sent against one invoice of 100, and twelve simultaneous issues against one draft. With the concurrency token disabled, all twelve payments were accepted and the draft was issued twelve times, three runs out of three. With it in place, at most three payments land and the draft is issued once.
- Twenty drafts issued at once must come back with twenty consecutive numbers, and an issue that fails after taking a number must leave the next invoice with the very next one.
- One test enumerates every mapped route and calls it with no token. Anything other than the three public routes that does not answer `401` fails it. With the fallback policy removed and one route left without a policy, the test failed and named that route.
- Tokens that are expired, for another audience, from another issuer, signed with another key, unsigned, or garbage are each refused, and a token built the same way with nothing wrong is accepted. With lifetime validation turned off only the expired case failed, and with audience validation turned off only the audience case failed.
- A table of 22 role and route pairs checks that each role reaches its own routes and gets `403` on the others.
- A balance read through the close snapshot is compared with a direct sum over the journal on five dates around a close. With the snapshot day counted twice, that test failed with 140 against 120.
- One test migrates a database to the previous schema, inserts an entry by hand, runs the new migration and checks the date reached the lines. With the backfill removed, the migration itself failed on the foreign key.
- The balance sheet of everything every test has posted must balance.
- Four statements of five identical lines are processed in parallel against five identical ledger lines. Exactly five bank lines end up matched, each to a different ledger line, and all four statements finish. With the race handling disabled the same test failed three runs of three on the unique index, so the race is real and the retry is what absorbs it.
- A statement left in `Processing` with an hour-old claim is finished by the worker. With stale claims no longer reclaimed, that test timed out.
- An overlapping statement adds only its new line. With the duplicate check removed, it failed instead.
- Twelve statements drained by six parallel processors plus the hosted worker each show exactly one attempt.
- One test bypasses the API and inserts a negative amount with raw SQL, to show the check constraint refuses it independently of the application.
- One domain test posts 500 random entries with random reversals from three fixed seeds and asserts the trial balance still balances.
- CI fails if the EF Core model has changes that no migration covers.

## Limitations

- The tuning numbers come from one desktop, a warm cache, one user and synthetic data. Write cost was not measured.
- An account statement returns at most 1,000 lines and says when it was cut short. There is no paging.
- There are no year-end closing entries, so the balance sheet carries all earnings as one current figure.
- Matching is one bank line to one ledger line with equal amounts. A deposit that covers three ledger lines, or a ledger line settled by two bank lines, has to be handled outside it.
- An entry and its reversal on a bank account both stay on the unreconciled list, although they net to zero.
- The reconciliation totals agree only for a date on or after both sides of every match.
- The statement format is one fixed CSV layout. No bank's real export format, OFX or MT940 is supported.
- The worker polls. There is no notification when a statement arrives, so the delay is up to one poll interval.
- The API is its own user store and token issuer with one symmetric key. There is no external identity provider, no refresh token, no key rotation, no lockout and no rate limit on `/auth/token`.
- A token stays valid until it expires. Deactivating a user or changing their roles takes effect at their next sign-in, up to 30 minutes later.
- Segregation of duties covers issuing an invoice only. Nothing stops an approver reversing their own journal entry.
- Rows written before the audit columns existed carry an empty author.
- An entry has one currency and every account on it must hold that currency. There is no conversion.
- A payment must be allocated in full. There is no unapplied cash, no credit notes, no refunds, and no accounts payable side.
- A payment that loses a concurrency race is refused, not retried by the server.
- A close cannot be reopened, and there are no year-end closing entries moving revenue and expense into retained earnings.
- Every post takes a shared lock on one row. Posts do not block each other, but a close waits for all of them and they wait for it.
- Append-only is enforced by the API surface, not by database permissions or triggers.
- A refused request does not consume its idempotency key, so only successful posts are replayed.
- Not deployed anywhere. The evidence is this repository and its test run.
