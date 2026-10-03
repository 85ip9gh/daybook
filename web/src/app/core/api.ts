import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type { Role } from './auth';
import { AppConfig } from './config';

export type AccountType = 'Asset' | 'Liability' | 'Equity' | 'Revenue' | 'Expense';
export type EntrySide = 'Debit' | 'Credit';

export interface Account {
  id: string;
  code: string;
  name: string;
  type: AccountType;
  currency: string;
  isActive: boolean;
}

export interface TrialBalanceRow {
  accountId: string;
  code: string;
  name: string;
  type: AccountType;
  debit: number;
  credit: number;
}

export interface TrialBalance {
  asOf: string;
  rows: TrialBalanceRow[];
  totalDebit: number;
  totalCredit: number;
}

export interface StatementLine {
  journalEntryId: string;
  lineNumber: number;
  entryDate: string;
  description: string;
  side: EntrySide;
  amount: number;
  balance: number;
}

export interface AccountStatement {
  accountId: string;
  from: string;
  to: string;
  openingBalance: number;
  lines: StatementLine[];
  closingBalance: number;
  truncated: boolean;
}

export interface EntryLine {
  lineNumber: number;
  accountId: string;
  side: EntrySide;
  amount: number;
}

export interface Entry {
  id: string;
  entryDate: string;
  description: string;
  currency: string;
  postedBy: string;
  lines: EntryLine[];
}

export interface NewEntry {
  entryDate: string;
  description: string;
  currency: string;
  lines: { accountId: string; side: EntrySide; amount: number }[];
}

export interface AgedCustomer {
  customerId: string;
  code: string;
  name: string;
  current: number;
  days1To30: number;
  days31To60: number;
  days61To90: number;
  over90: number;
  total: number;
}

export interface AgedReceivables {
  asOf: string;
  customers: AgedCustomer[];
  total: number;
}

export interface PeriodClose {
  periodEnd: string;
  closedAt: string;
  closedBy: string;
}

export interface Periods {
  closedThrough: string | null;
  closes: PeriodClose[];
}

export interface Customer {
  id: string;
  code: string;
  name: string;
}

export type InvoiceStatus = 'Draft' | 'Issued' | 'Paid' | 'Void';

export interface InvoiceLine {
  lineNumber: number;
  description: string;
  quantity: number;
  unitPrice: number;
  revenueAccountId: string;
  amount: number;
}

export interface Invoice {
  id: string;
  customerId: string;
  status: InvoiceStatus;
  number: number | null;
  currency: string;
  issueDate: string | null;
  dueDate: string | null;
  subtotal: number;
  tax: number;
  total: number;
  amountPaid: number;
  outstanding: number;
  journalEntryId: string | null;
  lines: InvoiceLine[];
  draftedBy: string;
  issuedBy: string | null;
  voidedBy: string | null;
}

export interface NewInvoice {
  customerId: string;
  currency: string;
  receivableAccountId: string;
  lines: { description: string; quantity: number; unitPrice: number; revenueAccountId: string }[];
  tax: { rate: number; accountId: string } | null;
}

export interface Receivables {
  customerId: string;
  outstanding: number;
  invoices: Invoice[];
}

export interface NewPayment {
  customerId: string;
  paymentDate: string;
  amount: number;
  currency: string;
  depositAccountId: string;
  allocations: { invoiceId: string; amount: number }[];
}

export interface Payment {
  id: string;
  paymentDate: string;
  amount: number;
  receivedBy: string;
  allocations: { invoiceId: string; amount: number }[];
}

export type StatementStatus = 'Pending' | 'Processing' | 'Processed' | 'Failed';

export interface BankStatement {
  id: string;
  accountId: string;
  status: StatementStatus;
  lineCount: number;
  newLineCount: number;
  matchedCount: number;
  error: string | null;
  importedBy: string;
  importedAt: string;
}

export interface BankLine {
  id: string;
  date: string;
  reference: string;
  description: string;
  amount: number;
}

export interface LedgerLine {
  journalEntryId: string;
  lineNumber: number;
  entryDate: string;
  amount: number;
}

export interface Reconciliation {
  accountId: string;
  asOf: string;
  bookBalance: number;
  bankTotal: number;
  unmatchedBankLines: BankLine[];
  unmatchedBankTotal: number;
  unreconciledLedgerLines: LedgerLine[];
  unreconciledLedgerTotal: number;
}

export interface User {
  id: string;
  userName: string;
  roles: Role[];
  isActive: boolean;
}

export interface Problem {
  status: number;
  code: string;
  detail: string;
}

export function toProblem(error: unknown): Problem {
  if (error instanceof HttpErrorResponse) {
    const body = (error.error ?? {}) as { code?: string; detail?: string; title?: string };
    return {
      status: error.status,
      code: body.code ?? '',
      detail:
        body.detail ??
        body.title ??
        (error.status === 0
          ? 'The server could not be reached.'
          : error.status === 403
            ? 'Your role does not allow this.'
            : `The request failed with status ${error.status}.`),
    };
  }

  return { status: 0, code: '', detail: 'Something went wrong.' };
}

@Injectable({ providedIn: 'root' })
export class Api {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);

  private url(path: string): string {
    return `${this.config.apiBase}${path}`;
  }

  accounts(): Observable<Account[]> {
    return this.http.get<Account[]>(this.url('/accounts'));
  }

  openAccount(account: Pick<Account, 'code' | 'name' | 'type' | 'currency'>): Observable<Account> {
    return this.http.post<Account>(this.url('/accounts'), account);
  }

  trialBalance(asOf: string): Observable<TrialBalance> {
    return this.http.get<TrialBalance>(this.url('/trial-balance'), { params: { asOf } });
  }

  statement(accountId: string, from: string, to: string): Observable<AccountStatement> {
    return this.http.get<AccountStatement>(this.url(`/accounts/${accountId}/statement`), {
      params: { from, to },
    });
  }

  postEntry(entry: NewEntry, idempotencyKey: string): Observable<Entry> {
    return this.http.post<Entry>(this.url('/journal-entries'), entry, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  agedReceivables(asOf: string): Observable<AgedReceivables> {
    return this.http.get<AgedReceivables>(this.url('/reports/aged-receivables'), {
      params: { asOf },
    });
  }

  periods(): Observable<Periods> {
    return this.http.get<Periods>(this.url('/periods'));
  }

  closePeriod(year: number, month: number): Observable<PeriodClose> {
    return this.http.post<PeriodClose>(this.url(`/periods/${year}/${month}/close`), null);
  }

  customers(): Observable<Customer[]> {
    return this.http.get<Customer[]>(this.url('/customers'));
  }

  registerCustomer(customer: Pick<Customer, 'code' | 'name'>): Observable<Customer> {
    return this.http.post<Customer>(this.url('/customers'), customer);
  }

  receivables(customerId: string): Observable<Receivables> {
    return this.http.get<Receivables>(this.url(`/customers/${customerId}/receivables`));
  }

  invoices(status: InvoiceStatus | ''): Observable<Invoice[]> {
    return this.http.get<Invoice[]>(this.url('/invoices'), { params: status ? { status } : {} });
  }

  invoice(id: string): Observable<Invoice> {
    return this.http.get<Invoice>(this.url(`/invoices/${id}`));
  }

  draftInvoice(invoice: NewInvoice): Observable<Invoice> {
    return this.http.post<Invoice>(this.url('/invoices'), invoice);
  }

  issueInvoice(id: string, issueDate: string, dueDate: string): Observable<Invoice> {
    return this.http.post<Invoice>(this.url(`/invoices/${id}/issue`), { issueDate, dueDate });
  }

  voidInvoice(id: string, voidDate: string): Observable<Invoice> {
    return this.http.post<Invoice>(this.url(`/invoices/${id}/void`), { voidDate });
  }

  receivePayment(payment: NewPayment, idempotencyKey: string): Observable<Payment> {
    return this.http.post<Payment>(this.url('/payments'), payment, {
      headers: { 'Idempotency-Key': idempotencyKey },
    });
  }

  reconciliation(accountId: string, asOf: string): Observable<Reconciliation> {
    return this.http.get<Reconciliation>(this.url(`/bank-accounts/${accountId}/reconciliation`), {
      params: { asOf },
    });
  }

  statements(accountId: string): Observable<BankStatement[]> {
    return this.http.get<BankStatement[]>(this.url(`/bank-accounts/${accountId}/statements`));
  }

  bankStatement(id: string): Observable<BankStatement> {
    return this.http.get<BankStatement>(this.url(`/bank-statements/${id}`));
  }

  uploadStatement(accountId: string, csv: string): Observable<BankStatement> {
    return this.http.post<BankStatement>(this.url(`/bank-accounts/${accountId}/statements`), csv, {
      headers: { 'Content-Type': 'text/csv' },
    });
  }

  matchBankLine(bankLineId: string, journalEntryId: string, lineNumber: number): Observable<void> {
    return this.http.post<void>(this.url(`/bank-lines/${bankLineId}/match`), {
      journalEntryId,
      lineNumber,
    });
  }

  users(): Observable<User[]> {
    return this.http.get<User[]>(this.url('/users'));
  }

  createUser(userName: string, password: string, roles: Role[]): Observable<User> {
    return this.http.post<User>(this.url('/users'), { userName, password, roles });
  }

  assignRoles(id: string, roles: Role[]): Observable<User> {
    return this.http.put<User>(this.url(`/users/${id}/roles`), { roles });
  }

  deactivateUser(id: string): Observable<User> {
    return this.http.post<User>(this.url(`/users/${id}/deactivate`), null);
  }
}
