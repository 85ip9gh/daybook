export interface Term {
  term: string;
  meaning: string;
}

export interface Topic {
  title: string;
  purpose: string;
  terms: Term[];
}

export const basics: Term[] = [
  { term: 'The books', meaning: 'The firm’s record of all its money.' },
  { term: 'Account', meaning: 'A named running total of money, such as Bank, Rent or Sales.' },
  {
    term: 'Debit and credit',
    meaning: 'Every amount is recorded twice, once as a debit and once as a credit.',
  },
  { term: 'Debit', meaning: 'Left column. Adds to what the firm owns or has spent.' },
  {
    term: 'Credit',
    meaning: 'Right column. Adds to what the firm owes, has earned, or the owner put in.',
  },
  { term: 'Balance', meaning: 'An account’s current total.' },
  { term: 'Post', meaning: 'Save to the books permanently.' },
  { term: 'Asset', meaning: 'What the firm owns or is owed, such as cash.' },
  { term: 'Liability', meaning: 'What the firm owes, such as a loan.' },
  { term: 'Equity', meaning: 'The owner’s share of the firm.' },
  { term: 'Revenue', meaning: 'Money earned from work.' },
  { term: 'Expense', meaning: 'Money spent to run the firm, such as rent.' },
  { term: 'Role', meaning: 'A person’s job. It sets what they can see and do.' },
];

export const topics: Record<string, Topic> = {
  'sign-in': {
    title: 'Sign in',
    purpose:
      'Sign in, or press a demo button to try the app as that person. The demo resets every night.',
    terms: [
      { term: 'Viewer', meaning: 'Sees everything, changes nothing.' },
      {
        term: 'Clerk',
        meaning: 'Adds customers, drafts invoices, records payments, uploads bank statements.',
      },
      {
        term: 'Approver',
        meaning: 'Makes invoices final, records entries in the books, matches bank lines.',
      },
      { term: 'Controller', meaning: 'Adds accounts and locks finished months.' },
      { term: 'Admin', meaning: 'Manages logins. Cannot see any money.' },
      {
        term: 'Two people per invoice',
        meaning: 'Whoever drafts an invoice cannot make it final.',
      },
    ],
  },
  'trial-balance': {
    title: 'Trial balance',
    purpose: 'Every account’s total on one date. The two column totals must match.',
    terms: [
      { term: 'As of', meaning: 'The date the totals are for.' },
      { term: 'Code', meaning: 'The account’s number.' },
      { term: 'Type', meaning: 'Asset, Liability, Equity, Revenue or Expense.' },
    ],
  },
  accounts: {
    title: 'Accounts',
    purpose: 'All accounts. Click one to see its history.',
    terms: [
      { term: 'Code', meaning: 'The account’s number.' },
      { term: 'Type', meaning: 'Asset, Liability, Equity, Revenue or Expense.' },
      { term: 'Currency', meaning: 'The money it is kept in, such as CAD.' },
      { term: 'Inactive', meaning: 'Can no longer take new entries.' },
      {
        term: 'Bank account',
        meaning:
          'An account the bank sends statements for. Only these appear on Bank reconciliation.',
      },
      { term: 'Open an account', meaning: 'Add an account. Controller only.' },
    ],
  },
  'accounts/:id': {
    title: 'Account statement',
    purpose: 'Every change to one account between two dates, with a running total.',
    terms: [
      { term: 'Opening balance', meaning: 'The total before the first date.' },
      { term: 'Balance', meaning: 'The total after each line.' },
      { term: 'Closing balance', meaning: 'The total at the end.' },
    ],
  },
  'journal/new': {
    title: 'New journal entry',
    purpose: 'Record an amount in the books by hand, such as rent paid. Approver only.',
    terms: [
      { term: 'Line', meaning: 'One account and one amount.' },
      { term: 'Example', meaning: 'Rent paid from the bank: Rent, debit 500. Bank, credit 500.' },
      {
        term: 'Debits must equal credits',
        meaning: 'Both columns must add up to the same total, or it will not save.',
      },
      { term: 'Post entry', meaning: 'Save it permanently.' },
      { term: 'Closed month', meaning: 'An entry cannot be dated in a locked month.' },
    ],
  },
  'receivables/aged': {
    title: 'Aged receivables',
    purpose: 'What each customer owes, grouped by how late it is.',
    terms: [
      { term: 'Receivable', meaning: 'Money a customer owes for work already billed.' },
      { term: 'As of', meaning: 'The date this is for.' },
      { term: 'Not yet due', meaning: 'Owed, but the due date has not passed.' },
      { term: '1 to 30, 31 to 60, 61 to 90, Over 90', meaning: 'Days past the due date.' },
    ],
  },
  'receivables/customers': {
    title: 'Customers',
    purpose: 'The firm’s customers. Click one to see what they owe or to record a payment.',
    terms: [
      { term: 'Code', meaning: 'The customer’s short ID.' },
      { term: 'Register a customer', meaning: 'Add a customer. Clerk only.' },
    ],
  },
  'receivables/customers/:id': {
    title: 'Customer',
    purpose: 'This customer’s unpaid invoices. Record a payment from them here.',
    terms: [
      { term: 'Due', meaning: 'The date payment is expected.' },
      { term: 'Outstanding', meaning: 'Still unpaid.' },
      {
        term: 'Paying now',
        meaning:
          'How much of the payment goes to each invoice. It must add up to the full payment.',
      },
      { term: 'Deposited to', meaning: 'The bank account the money went into.' },
    ],
  },
  'receivables/invoices': {
    title: 'Invoices',
    purpose: 'All invoices, drafts first.',
    terms: [
      { term: 'Draft', meaning: 'Not final. No number, not in the books.' },
      { term: 'Issued', meaning: 'Final. Numbered and in the books.' },
      { term: 'Paid', meaning: 'Fully paid.' },
      { term: 'Void', meaning: 'Cancelled. Only unpaid invoices can be cancelled.' },
      { term: 'Outstanding', meaning: 'Still unpaid.' },
      { term: 'New invoice', meaning: 'Start a draft. Clerk only.' },
    ],
  },
  'receivables/invoices/new': {
    title: 'New invoice',
    purpose: 'Draft an invoice. A different person must make it final.',
    terms: [
      { term: 'Unit price', meaning: 'The price of one.' },
      { term: 'Amount', meaning: 'Quantity times unit price.' },
      { term: 'Revenue account', meaning: 'The account this income is recorded in.' },
      { term: 'Receivable account', meaning: 'The account for money customers owe.' },
      { term: 'Tax', meaning: 'Sales tax added on top, such as HST.' },
      { term: 'Subtotal', meaning: 'The total before tax.' },
      { term: 'Save draft', meaning: 'Save it without making it final.' },
    ],
  },
  'receivables/invoices/:id': {
    title: 'Invoice',
    purpose: 'One invoice in full. The Approver makes drafts final or cancels unpaid ones here.',
    terms: [
      { term: 'Drafted by, Issued by, Voided by', meaning: 'Who did each step.' },
      {
        term: 'Issue this invoice',
        meaning: 'Make it final. It gets a number and goes into the books.',
      },
      { term: 'Void this invoice', meaning: 'Cancel it.' },
      { term: 'Outstanding', meaning: 'Still unpaid.' },
    ],
  },
  banking: {
    title: 'Bank reconciliation',
    purpose: 'Check the books against the bank statement, line by line.',
    terms: [
      {
        term: 'Balance in the books',
        meaning: 'The account’s total in Daybook on the As of date.',
      },
      {
        term: 'Total of uploaded bank lines',
        meaning:
          'Every uploaded bank line added up. Not the bank’s balance unless every statement was uploaded.',
      },
      {
        term: 'At the bank only, In the books only',
        meaning: 'Totals of the unpaired lines. Both 0 means the books and the bank agree.',
      },
      {
        term: 'Import a statement',
        meaning: 'Upload the bank’s transaction file (CSV). Clerk only.',
      },
      {
        term: 'Status',
        meaning: 'Pending: waiting. Processing: being read. Processed: done. Failed: unreadable.',
      },
      {
        term: 'Matched',
        meaning: 'Same amount in the bank and the books, within three days. Paired automatically.',
      },
      {
        term: 'At the bank, not in the books',
        meaning: 'Not recorded in the books yet, such as a bank fee.',
      },
      {
        term: 'In the books, not at the bank',
        meaning: 'Not through the bank yet, such as a payment on its way.',
      },
      { term: 'Match by hand', meaning: 'Pair two equal amounts yourself. Approver only.' },
      {
        term: 'Undo match',
        meaning: 'Split a pair. Both lines go back to unpaired. Approver only.',
      },
      { term: 'Reference', meaning: 'The bank’s ID for the line.' },
    ],
  },
  periods: {
    title: 'Periods',
    purpose: 'Lock finished months so nothing in them can change.',
    terms: [
      { term: 'Period', meaning: 'One month.' },
      { term: 'Close a period', meaning: 'Lock a month. Controller only.' },
      { term: 'Closed through', meaning: 'The last locked date.' },
      {
        term: 'This cannot be undone',
        meaning: 'Locked months stay locked. Earlier open months lock too.',
      },
    ],
  },
  'admin/users': {
    title: 'Users',
    purpose: 'Create logins and set each person’s role. Admin only.',
    terms: [
      { term: 'Roles', meaning: 'Changes apply the next time the person signs in.' },
      { term: 'Password', meaning: '12 characters or more.' },
      { term: 'Deactivated', meaning: 'Can no longer sign in.' },
    ],
  },
  'no-access': {
    title: 'No access',
    purpose: 'Your login has no role yet. Ask an Admin for one.',
    terms: [],
  },
};
