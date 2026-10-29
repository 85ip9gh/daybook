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
  {
    term: 'Daybook',
    meaning:
      'Practice accounting software for a made-up small consulting firm. It keeps track of the firm’s money: what it earns, spends, owns and owes.',
  },
  {
    term: 'The books',
    meaning: 'The firm’s full written record of every bit of money coming in and going out.',
  },
  {
    term: 'Account',
    meaning:
      'A named bucket in the books for one kind of money, like Rent, Consulting revenue or the bank account. It is not a login.',
  },
  {
    term: 'Debit and credit',
    meaning:
      'Every money movement is written down twice, once on the debit side and once on the credit side, for the same amount. That is why the books always add up, and why a mistake stands out.',
  },
  {
    term: 'Debit',
    meaning:
      'The left-hand column. It makes things the firm owns, and money it has spent, go up. Example: cash coming into the bank is a debit to the bank account.',
  },
  {
    term: 'Credit',
    meaning:
      'The right-hand column. It makes money the firm owes, money it has earned, and the owner’s share go up. Example: earning a fee is a credit to revenue.',
  },
  {
    term: 'Balance',
    meaning: 'What is in an account right now: all its debits and credits added together.',
  },
  {
    term: 'Post',
    meaning:
      'Save something into the books for good. Posted entries are never edited or deleted. A mistake is fixed by posting a new entry that cancels it, so the history stays honest.',
  },
  {
    term: 'Asset',
    meaning:
      'Something the firm owns or is owed, like money in the bank, equipment, or money a customer still has to pay.',
  },
  {
    term: 'Liability',
    meaning:
      'Something the firm owes to someone else, like a bank loan or sales tax it collected for the government.',
  },
  {
    term: 'Equity',
    meaning: 'The owner’s share of the firm: what the owner put in, plus what the firm has kept.',
  },
  {
    term: 'Revenue',
    meaning: 'Money the firm earned by doing its work.',
  },
  {
    term: 'Expense',
    meaning: 'Money the firm spent to keep running, like rent, salaries and bank fees.',
  },
  {
    term: 'Role',
    meaning:
      'A person’s job in the firm: Viewer, Clerk, Approver, Controller or Admin. It decides which screens they see and what they can change.',
  },
];

export const topics: Record<string, Topic> = {
  'sign-in': {
    title: 'Sign in',
    purpose:
      'The front door. Type a user name and password, or press a demo button to look around as that person. The demo books are made up and reset every night, so nothing you do here can break anything.',
    terms: [
      {
        term: 'Demo buttons',
        meaning: 'Each one signs you in as a different person in the firm. No password needed.',
      },
      { term: 'Viewer', meaning: 'Can look at everything and change nothing.' },
      {
        term: 'Clerk',
        meaning:
          'Does the day-to-day typing: adds customers, writes draft invoices, records payments that arrive, and uploads bank statements.',
      },
      {
        term: 'Approver',
        meaning:
          'Signs things off: makes draft invoices official, cancels unpaid ones, writes entries in the books, and pairs bank lines by hand.',
      },
      {
        term: 'Controller',
        meaning: 'Adds new accounts and locks each month once it is finished.',
      },
      {
        term: 'Admin',
        meaning:
          'Gives people logins and jobs, and cannot see any money. There is no demo button for it.',
      },
      {
        term: 'Two people per invoice',
        meaning:
          'The person who writes a bill can never be the one who approves it. That way nobody can make up a bill and push it through alone.',
      },
    ],
  },
  'trial-balance': {
    title: 'Trial balance',
    purpose:
      'A one-page check that the books add up. It lists every account with its total. The two totals at the bottom must be equal.',
    terms: [
      {
        term: 'As of',
        meaning:
          'The day you are looking at. Pick an earlier date to see the books as they were then.',
      },
      {
        term: 'Code',
        meaning:
          'A short number for each account, like 1000 for the main bank account. Numbers keep accounts in a standard order: assets first, then what is owed, then the owner’s share, earnings and costs.',
      },
      {
        term: 'Type',
        meaning:
          'Which family the account belongs to: Asset, Liability, Equity, Revenue or Expense.',
      },
      {
        term: 'Debit and Credit columns',
        meaning:
          'Each account’s total sits in whichever column is bigger for it. Bank and expenses usually show as debits; loans, tax owed and earnings usually show as credits.',
      },
      {
        term: 'Totals',
        meaning:
          'The two numbers at the bottom. When they match, every entry was written down on both sides correctly.',
      },
    ],
  },
  accounts: {
    title: 'Accounts',
    purpose:
      'The list of every bucket the firm sorts its money into. Click one to see everything that happened in it. The Controller can add new accounts here.',
    terms: [
      {
        term: 'Chart of accounts',
        meaning: 'The full list of accounts, like the labelled drawers of a filing cabinet.',
      },
      { term: 'Code', meaning: 'A short number for the account, used to keep the list in order.' },
      { term: 'Name', meaning: 'What the account is for, in words.' },
      {
        term: 'Type',
        meaning: 'Asset, Liability, Equity, Revenue or Expense. See the basic words below.',
      },
      { term: 'Currency', meaning: 'The kind of money the account is kept in, like CAD.' },
      {
        term: 'Active and Inactive',
        meaning:
          'Active accounts can be used. Inactive ones stay for the record but take no new money.',
      },
      {
        term: 'Open an account',
        meaning: 'Add a new bucket to the list. Only the Controller can do this.',
      },
    ],
  },
  'accounts/:id': {
    title: 'Account statement',
    purpose:
      'Everything that happened in one account between two dates, in date order, with a running total. Like a bank statement, but for any account in the books.',
    terms: [
      { term: 'From and To', meaning: 'The first and last day to show.' },
      { term: 'Opening balance', meaning: 'What was in the account just before the first day.' },
      { term: 'Date', meaning: 'The day the money moved.' },
      { term: 'Description', meaning: 'A short note saying what the money was for.' },
      { term: 'Debit and Credit', meaning: 'Which side of the account this line went on.' },
      { term: 'Balance', meaning: 'The running total after each line.' },
      { term: 'Closing balance', meaning: 'What was in the account at the end of the last day.' },
    ],
  },
  'journal/new': {
    title: 'New journal entry',
    purpose:
      'Write a money movement into the books by hand. Invoices and payments write their own entries, so this page is for everything else, like paying rent. Only the Approver can use it.',
    terms: [
      {
        term: 'Journal entry',
        meaning: 'One record of one money movement: a date, a short note, and two or more lines.',
      },
      {
        term: 'Line',
        meaning: 'One account and one amount, on either the debit side or the credit side.',
      },
      {
        term: 'Example',
        meaning: 'Paying $500 rent from the bank: Rent, debit 500. Bank account, credit 500.',
      },
      {
        term: 'Debits must equal credits',
        meaning:
          'Both sides have to add up to the same amount, or the entry will not save. This is what keeps the books balanced.',
      },
      {
        term: 'Post entry',
        meaning: 'Save it into the books for good. It cannot be edited or deleted afterwards.',
      },
      {
        term: 'Closed month',
        meaning: 'An entry cannot be dated in a month that has already been locked.',
      },
    ],
  },
  'receivables/aged': {
    title: 'Aged receivables',
    purpose:
      'Who owes the firm money, and how late they are. The older a debt, the harder it is to collect, so this shows who to chase first.',
    terms: [
      { term: 'Receivable', meaning: 'Money a customer owes the firm for work already billed.' },
      { term: 'Aged', meaning: 'Sorted by how long it has been since the money was due.' },
      { term: 'As of', meaning: 'Shows what was owed on that day.' },
      { term: 'Not yet due', meaning: 'Billed, but the due date has not come yet.' },
      {
        term: '1 to 30, 31 to 60, 61 to 90, Over 90',
        meaning: 'How many days late the money is.',
      },
      { term: 'Total', meaning: 'Everything that customer owes, late or not.' },
    ],
  },
  'receivables/customers': {
    title: 'Customers',
    purpose:
      'The people and companies the firm bills for its work. Click one to see what they owe and to record a payment.',
    terms: [
      { term: 'Customer', meaning: 'Someone the firm sends bills to.' },
      { term: 'Code', meaning: 'A short ID for the customer.' },
      {
        term: 'Register a customer',
        meaning: 'Add a new customer. Only the Clerk can do this.',
      },
    ],
  },
  'receivables/customers/:id': {
    title: 'Customer',
    purpose:
      'One customer’s unpaid bills. When their money arrives, the Clerk records it here and says which bills it pays off.',
    terms: [
      { term: 'Open invoice', meaning: 'A bill that has not been fully paid yet.' },
      { term: 'Issued', meaning: 'The day the bill was made official.' },
      { term: 'Due', meaning: 'The day the money is expected.' },
      { term: 'Total', meaning: 'What the bill was for.' },
      { term: 'Outstanding', meaning: 'What is still left to pay on it.' },
      {
        term: 'Paying now',
        meaning:
          'How much of this payment goes to each bill. Every dollar has to go to a bill, with nothing left over.',
      },
      { term: 'Payment date', meaning: 'The day the money arrived.' },
      { term: 'Deposited to', meaning: 'Which of the firm’s bank accounts the money went into.' },
    ],
  },
  'receivables/invoices': {
    title: 'Invoices',
    purpose:
      'Every bill the firm has written, from first draft to fully paid. Drafts are listed first, then the newest bills.',
    terms: [
      { term: 'Invoice', meaning: 'A bill sent to a customer for work done.' },
      {
        term: 'Draft',
        meaning: 'Written but not official. It has no number yet and does not touch the books.',
      },
      {
        term: 'Issued',
        meaning:
          'Approved and official. It gets the next invoice number, and the money owed goes into the books.',
      },
      { term: 'Paid', meaning: 'The customer has paid all of it.' },
      {
        term: 'Void',
        meaning: 'Cancelled. Only a bill with no payment on it can be cancelled.',
      },
      {
        term: 'Invoice number',
        meaning: 'Given in order with no gaps, so a missing bill would stand out.',
      },
      { term: 'Outstanding', meaning: 'What is still unpaid.' },
      { term: 'New invoice', meaning: 'Start a draft. Only the Clerk can do this.' },
    ],
  },
  'receivables/invoices/new': {
    title: 'New invoice',
    purpose:
      'Write a new bill as a draft. A different person, the Approver, has to make it official.',
    terms: [
      { term: 'Customer', meaning: 'Who the bill is for.' },
      { term: 'Line', meaning: 'One thing you are charging for.' },
      { term: 'Description', meaning: 'What the work was, in words.' },
      { term: 'Quantity', meaning: 'How many, like hours of work.' },
      { term: 'Unit price', meaning: 'The price of one.' },
      { term: 'Amount', meaning: 'Quantity times unit price.' },
      {
        term: 'Revenue account',
        meaning: 'Which earnings bucket this work counts toward, like Consulting revenue.',
      },
      {
        term: 'Receivable account',
        meaning: 'The bucket that keeps track of money customers owe.',
      },
      {
        term: 'Tax',
        meaning:
          'Sales tax added on top, like HST. Pick No tax if none applies. It is worked out once, on the subtotal.',
      },
      { term: 'Subtotal', meaning: 'The bill before tax.' },
      { term: 'Total', meaning: 'What the customer pays: subtotal plus tax.' },
      {
        term: 'Save draft',
        meaning: 'Keeps the bill without making it official. Another user must issue it.',
      },
    ],
  },
  'receivables/invoices/:id': {
    title: 'Invoice',
    purpose:
      'One bill in full: what was charged, who wrote it, who approved it, and what is still owed. The Approver makes drafts official, or cancels unpaid bills, here.',
    terms: [
      {
        term: 'Drafted by, Issued by, Voided by',
        meaning: 'Who did each step. Every action is signed with a name.',
      },
      {
        term: 'Issue this invoice',
        meaning:
          'Make it official. It gets the next number and the money owed goes into the books.',
      },
      { term: 'Issue date', meaning: 'The day it becomes official.' },
      { term: 'Due date', meaning: 'The day the customer should pay by.' },
      {
        term: 'Void this invoice',
        meaning:
          'Cancel it. The books get a new entry that undoes the old one, so the history shows both.',
      },
      { term: 'Subtotal and Total', meaning: 'The bill before tax, and with tax.' },
      { term: 'Outstanding', meaning: 'What is still unpaid.' },
    ],
  },
  banking: {
    title: 'Bank reconciliation',
    purpose:
      'Check the firm’s books against the bank’s own records, line by line. Every deposit and withdrawal should show up in both places. Anything that shows up in only one place needs a look.',
    terms: [
      {
        term: 'Reconciliation',
        meaning: 'Ticking off each bank line against the same money in the books until both agree.',
      },
      { term: 'Bank account', meaning: 'Which of the firm’s bank accounts you are checking.' },
      {
        term: 'Import a statement',
        meaning:
          'Upload the bank’s list of transactions as a CSV file, a simple spreadsheet file with date, reference, description and amount. Only the Clerk can do this.',
      },
      {
        term: 'Status',
        meaning:
          'Pending means the file is waiting, Processing means it is being read, Processed means it is done, Failed means it could not be read.',
      },
      {
        term: 'Matched',
        meaning:
          'A bank line and a book line that are the same money: the same amount, within three days. Daybook pairs these by itself.',
      },
      {
        term: 'At the bank, not in the books',
        meaning: 'The bank shows it but nobody wrote it down yet, like a bank fee.',
      },
      {
        term: 'In the books, not at the bank',
        meaning:
          'Written down, but the bank has not shown it yet, like a payment still on its way.',
      },
      {
        term: 'Match by hand',
        meaning:
          'Pair a bank line with a book line yourself when they belong together. The amounts must be equal. Only the Approver can do this.',
      },
      { term: 'Reference', meaning: 'The bank’s own ID for that line.' },
      {
        term: 'Balance in the books',
        meaning: 'What the books say is in this bank account on that day.',
      },
    ],
  },
  periods: {
    title: 'Periods',
    purpose:
      'Lock finished months. Once a month is closed, nothing dated in it can be added, so that month’s numbers stay the same forever.',
    terms: [
      { term: 'Period', meaning: 'A month of the books.' },
      { term: 'Close a period', meaning: 'Lock a month. Only the Controller can do this.' },
      {
        term: 'Closed through',
        meaning: 'The last locked day. Everything on or before it is final.',
      },
      { term: 'Closed on and Closed by', meaning: 'When the month was locked, and who did it.' },
      {
        term: 'This cannot be undone',
        meaning:
          'A closed month cannot be reopened. Closing a month also closes any earlier month that was still open.',
      },
    ],
  },
  'admin/users': {
    title: 'Users',
    purpose:
      'Give people a login and a job. Only the Admin sees this page, and the Admin cannot see any money.',
    terms: [
      { term: 'User', meaning: 'A person who can sign in.' },
      { term: 'User name', meaning: 'The name they type to sign in.' },
      { term: 'Password', meaning: 'At least 12 characters.' },
      {
        term: 'Roles',
        meaning:
          'Their job or jobs: Viewer, Clerk, Approver, Controller or Admin. A change takes effect the next time they sign in.',
      },
      { term: 'Status', meaning: 'A deactivated user can no longer sign in.' },
    ],
  },
  'no-access': {
    title: 'No access',
    purpose:
      'Your login has no job with any screens yet, so there is nothing to show. An Admin needs to give you a role.',
    terms: [],
  },
};
