import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
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
}
