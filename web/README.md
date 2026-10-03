# Daybook web

The front end for the Daybook API. Angular 21, standalone components, signals, no UI library.

## Design

Quiet and exact. It is meant to look like a ledger, not a dashboard.

- **One device: ruled lines.** Tables are hairline-ruled, totals sit under a single rule and over a double one, the way a column of figures is closed on paper. There are no cards, no shadows, no gradients and no icons.
- **Two typefaces.** IBM Plex Sans for text and IBM Plex Mono for every figure, code and date, so columns of numbers align digit for digit. Both are bundled, nothing is fetched from a font service.
- **One accent.** A dark green carries the primary action, the current navigation item and focus. Everything else is ink on warm paper.
- **Accounting conventions.** Amounts are right-aligned, a negative is written in parentheses, and a zero in a debit or credit column is left blank.
- **Tokens only.** Colour, type size and spacing all come from custom properties at the top of `src/styles.css`. Spacing is on a 4px grid.

Body text is `#191917` on `#faf9f6`. Every control has a visible hover and focus state. At phone width the side rail becomes a top bar, wide tables scroll inside their own frame, and the page itself never scrolls sideways.

## Screens

| Screen | Roles | What it does |
|---|---|---|
| Sign in | everyone | Exchanges a user name and password for a token |
| Trial balance | readers | Every account with activity as of a date, and whether it balances |
| Accounts | readers, Controller to open | The chart of accounts |
| Account statement | readers | Lines for a date range with a running balance |
| New journal entry | Approver | Lines with live totals, postable only when balanced |
| Customers | readers, Clerk to register | The customer list |
| Customer | readers, Clerk to receive | Open invoices, and a payment allocated across them |
| Invoices | readers | Every invoice, filtered by status |
| New invoice | Clerk | Lines, optional tax, live totals, saved as a draft |
| Invoice | readers, Approver to issue or void | Lines, totals, who drafted and issued it |
| Aged receivables | readers | What each customer owed on a date, by how overdue |
| Bank reconciliation | readers, Clerk to import, Approver to match | Both unreconciled sides, statement import, manual match |
| Periods | readers, Controller to close | The closed-through date, and a two-step close |
| Users | Admin | Add a user, change roles, deactivate |

Navigation shows only what a role can open, and routes are guarded. That is convenience: the API enforces every rule itself.

## Details worth knowing

- **Money is summed in whole cents.** Amounts typed into the journal form are parsed from text into integers before they are added, so `0.10 + 0.20` balances against `0.30`.
- **A retry is safe.** The journal and payment forms each keep one idempotency key until a post succeeds. A failed post retried sends the same key, and the API answers with the original instead of posting twice.
- **Invoice totals are previewed with the server's arithmetic.** Quantity times price is multiplied as integers and rounded half away from zero, and tax is rounded once on the subtotal, so the preview equals what the API stores. The API stays the authority.
- **A statement import is followed, not awaited.** The upload is answered at once, and the screen asks for the statement's status every second until the worker has finished.
- **An administrator cannot deactivate their own account from the screen.**
- **The token lives in `sessionStorage`.** It survives a reload and is gone when the tab closes. Any script running on the page could read it, which is the usual trade against an `HttpOnly` cookie.
- **A `401` signs the user out** and returns to the sign-in page. A `403` is shown in place.
- **The API address is read at start-up** from `public/config.json`, so one build can be pointed at any API without rebuilding.

## Running it

The API has to be running on port 5081. See the root README for that.

```bash
npm ci
```

```bash
npm start
```

The dev server proxies `/api` to `http://localhost:5081`, so there is no cross-origin request in development.

To fill an empty database with a small set of books and four demo users:

```bash
LEDGER_ADMIN_PASSWORD="$LEDGER_ADMIN_PASSWORD" node ../tools/seed-demo.mjs
```

The demo users' generated passwords are written to `.dev/demo-users.json` at the repository root, which is ignored by git.

## Checks

```bash
npm test
```

```bash
npm run build
```

```bash
npm run format:check
```

69 unit tests cover money parsing and formatting, the journal entry, invoice and payment rules, the token interceptor, role-based navigation, and the forms themselves: disabled until valid, sending the right lines, keeping a key across a failed post, and offering a match only when two amounts are equal.

## Not built yet

There is no dark theme. A match cannot be undone from the screen, although the API can. Journal entries can be posted but not browsed or reversed. Nothing warns before deactivating the last administrator.
