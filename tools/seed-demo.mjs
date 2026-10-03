import { randomBytes, randomUUID } from 'node:crypto';
import { mkdir, writeFile } from 'node:fs/promises';

const base = (process.env.LEDGER_API ?? 'http://localhost:5081').replace(/\/+$/, '');
const adminUser = process.env.LEDGER_ADMIN_USER ?? 'admin';
const adminPassword = process.env.LEDGER_ADMIN_PASSWORD;
const credentialsPath = process.env.LEDGER_DEMO_USERS ?? '.dev/demo-users.json';

if (!adminPassword) {
  console.error('Set LEDGER_ADMIN_PASSWORD to the first admin password.');
  process.exit(1);
}

async function call(token, method, path, body, headers = {}) {
  const response = await fetch(base + path, {
    method,
    headers: {
      ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...headers,
    },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  const text = await response.text();
  if (!response.ok) {
    throw new Error(`${method} ${path} answered ${response.status}: ${text}`);
  }

  return text ? JSON.parse(text) : null;
}

async function signIn(userName, password) {
  return (await call(null, 'POST', '/auth/token', { userName, password })).accessToken;
}

const admin = await signIn(adminUser, adminPassword);

const users = {
  'demo-viewer': ['Viewer'],
  'demo-clerk': ['Clerk'],
  'demo-approver': ['Approver'],
  'demo-controller': ['Controller'],
};

const credentials = {};
const tokens = {};
for (const [userName, roles] of Object.entries(users)) {
  const password = randomBytes(15).toString('base64url');
  await call(admin, 'POST', '/users', { userName, password, roles });
  credentials[userName] = { password, roles };
  tokens[userName] = await signIn(userName, password);
}

await mkdir(credentialsPath.split('/').slice(0, -1).join('/') || '.', { recursive: true });
await writeFile(credentialsPath, JSON.stringify(credentials, null, 2) + '\n');

const clerk = tokens['demo-clerk'];
const approver = tokens['demo-approver'];
const controller = tokens['demo-controller'];

const chart = [
  ['1000', 'Operating account', 'Asset'],
  ['1010', 'Savings account', 'Asset'],
  ['1200', 'Accounts receivable', 'Asset'],
  ['1500', 'Equipment', 'Asset'],
  ['2000', 'Accounts payable', 'Liability'],
  ['2100', 'HST payable', 'Liability'],
  ['2500', 'Bank loan', 'Liability'],
  ['3000', "Owner's capital", 'Equity'],
  ['4000', 'Consulting revenue', 'Revenue'],
  ['4100', 'Support contracts', 'Revenue'],
  ['5000', 'Salaries', 'Expense'],
  ['5100', 'Rent', 'Expense'],
  ['5200', 'Software subscriptions', 'Expense'],
  ['5300', 'Bank fees', 'Expense'],
];

const account = {};
for (const [code, name, type] of chart) {
  account[code] = (await call(controller, 'POST', '/accounts', { code, name, type, currency: 'CAD' })).id;
}

async function post(entryDate, description, debit, credit, amount) {
  await call(
    approver,
    'POST',
    '/journal-entries',
    {
      entryDate,
      description,
      currency: 'CAD',
      lines: [
        { accountId: account[debit], side: 'Debit', amount },
        { accountId: account[credit], side: 'Credit', amount },
      ],
    },
    { 'Idempotency-Key': randomUUID() },
  );
}

await post('2026-07-02', 'Owner investment', '1000', '3000', 50000);
await post('2026-07-06', 'Workstations and network equipment', '1500', '1000', 8412.6);
await post('2026-07-15', 'Bank loan drawdown', '1000', '2500', 20000);
await post('2026-07-20', 'Transfer to savings', '1010', '1000', 15000);

for (const month of ['07', '08', '09', '10']) {
  await post(`2026-${month}-01`, `Office rent, ${month}/2026`, '5100', '1000', 2300);
  await post(`2026-${month}-02`, 'Monthly account fee', '5300', '1000', 18.5);
}

for (const month of ['07', '08', '09']) {
  await post(`2026-${month}-15`, 'Payroll, first half', '5000', '1000', 6250);
  await post(`2026-${month}-28`, 'Payroll, second half', '5000', '1000', 6250);
  await post(`2026-${month}-10`, 'Hosting and developer tools', '5200', '2000', 412.37);
  await post(`2026-${month}-24`, 'Paid hosting and developer tools', '2000', '1000', 412.37);
}

const customers = {};
for (const [code, name] of [
  ['NWF', 'Northwind Fisheries'],
  ['HHT', 'Halifax Harbour Tours'],
  ['BNA', 'Bluenose Analytics'],
  ['FTP', 'Fundy Tidal Power'],
]) {
  customers[code] = (await call(clerk, 'POST', '/customers', { code, name })).id;
}

async function invoice(customer, issueDate, dueDate, revenue, lines) {
  const draft = await call(clerk, 'POST', '/invoices', {
    customerId: customers[customer],
    currency: 'CAD',
    receivableAccountId: account['1200'],
    lines: lines.map(([description, quantity, unitPrice]) => ({
      description,
      quantity,
      unitPrice,
      revenueAccountId: account[revenue],
    })),
    tax: { rate: 0.14, accountId: account['2100'] },
  });

  return call(approver, 'POST', `/invoices/${draft.id}/issue`, { issueDate, dueDate });
}

async function pay(customer, paymentDate, allocations) {
  const amount = allocations.reduce((total, [, value]) => total + Math.round(value * 100), 0) / 100;
  await call(
    clerk,
    'POST',
    '/payments',
    {
      customerId: customers[customer],
      paymentDate,
      amount,
      currency: 'CAD',
      depositAccountId: account['1000'],
      allocations: allocations.map(([issued, value]) => ({ invoiceId: issued.id, amount: value })),
    },
    { 'Idempotency-Key': randomUUID() },
  );
}

const first = await invoice('NWF', '2026-07-08', '2026-08-07', '4000', [['Catch reporting integration', 80, 145]]);
const second = await invoice('HHT', '2026-07-22', '2026-08-21', '4000', [['Booking system review', 24, 160]]);
const third = await invoice('BNA', '2026-08-05', '2026-09-04', '4100', [['Support contract, quarter three', 1, 4800]]);
const fourth = await invoice('FTP', '2026-08-18', '2026-09-17', '4000', [['Turbine telemetry pipeline', 120, 150], ['On-site commissioning', 2, 950]]);
const fifth = await invoice('NWF', '2026-09-12', '2026-10-12', '4100', [['Support contract, quarter four', 1, 3600]]);
await invoice('HHT', '2026-09-26', '2026-10-26', '4000', [['Seasonal schedule import', 18, 160]]);

await pay('NWF', '2026-08-04', [[first, first.total]]);
await pay('HHT', '2026-08-25', [[second, 2000]]);
await pay('FTP', '2026-09-20', [[fourth, 12000]]);
await pay('NWF', '2026-10-01', [[fifth, 1500]]);
void third;

await call(controller, 'POST', '/periods/2026/8/close');

console.log(`Seeded ${chart.length} accounts, 4 customers, 6 invoices and 4 payments. Books closed through 2026-08-31.`);
console.log(`Demo user passwords were written to ${credentialsPath}.`);
