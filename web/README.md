# Ledger web

The front end for the Ledger API. Angular 21, standalone components, signals, no UI library.

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
| Aged receivables | readers | What each customer owed on a date, by how overdue |
| Periods | readers, Controller to close | The closed-through date, and a two-step close |

Navigation shows only what a role can open, and routes are guarded. That is convenience: the API enforces every rule itself.

## Details worth knowing

- **Money is summed in whole cents.** Amounts typed into the journal form are parsed from text into integers before they are added, so `0.10 + 0.20` balances against `0.30`.
- **A retry is safe.** The journal form keeps one idempotency key until a post succeeds. A failed post retried sends the same key, and the API answers with the original entry instead of posting twice.
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

38 unit tests cover money parsing and formatting, the journal entry rules, the token interceptor, role-based navigation, and the journal form itself: disabled until balanced, posting the right lines, keeping its key across a failed post.

## Not built yet

Invoices, payments and bank reconciliation have no screens. User management has none either, so an administrator sees a page saying so. There is no dark theme.
