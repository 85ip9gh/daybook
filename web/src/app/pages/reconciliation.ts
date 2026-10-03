import { DatePipe } from '@angular/common';
import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { switchMap, take, takeWhile, timer } from 'rxjs';
import { Account, Api, BankStatement, Reconciliation, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

@Component({
  selector: 'app-reconciliation',
  imports: [FormsModule, MoneyPipe, DatePipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Bank reconciliation</h1>
          <p>What the bank shows that the books do not, and the other way round.</p>
        </div>
        <div class="controls">
          <label class="field">
            <span>Bank account</span>
            <select [ngModel]="accountId()" (ngModelChange)="choose($event)">
              <option value="">Choose an account</option>
              @for (account of banks(); track account.id) {
                <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span>As of</span>
            <input type="date" [ngModel]="asOf()" (ngModelChange)="asOf.set($event); refresh()" />
          </label>
        </div>
      </header>

      @if (!accountId()) {
        <p class="note">Choose a bank account to reconcile.</p>
      } @else {
        <div class="stack">
          @if (report().state === 'error') {
            <p class="note error" role="alert">{{ report().problem?.detail }}</p>
          } @else if (report().value; as found) {
            <dl class="figures ruled">
              <div>
                <dt>Balance in the books</dt>
                <dd>{{ found.bookBalance | money }}</dd>
              </div>
              <div>
                <dt>Imported from the bank</dt>
                <dd>{{ found.bankTotal | money }}</dd>
              </div>
              <div>
                <dt>At the bank only</dt>
                <dd>{{ found.unmatchedBankTotal | money }}</dd>
              </div>
              <div>
                <dt>In the books only</dt>
                <dd>{{ found.unreconciledLedgerTotal | money }}</dd>
              </div>
            </dl>

            <div class="split">
              <div>
                <div class="section-head">
                  <h2>At the bank, not in the books</h2>
                </div>
                @if (found.unmatchedBankLines.length === 0) {
                  <p class="note">Every imported bank line is matched.</p>
                } @else {
                  <div class="scroll">
                    <table class="ledger">
                      <thead>
                        <tr>
                          @if (canMatch) {
                            <th scope="col"><span class="visually-hidden">Choose</span></th>
                          }
                          <th scope="col">Date</th>
                          <th scope="col">Reference</th>
                          <th scope="col" class="figure">Amount</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (line of found.unmatchedBankLines; track line.id) {
                          <tr>
                            @if (canMatch) {
                              <td class="pick">
                                <input
                                  type="radio"
                                  name="bankLine"
                                  [attr.aria-label]="'Bank line ' + line.reference"
                                  [checked]="bankLineId() === line.id"
                                  (change)="bankLineId.set(line.id)"
                                />
                              </td>
                            }
                            <td class="code">{{ line.date }}</td>
                            <td>
                              {{ line.reference }}
                              <span class="muted">{{ line.description }}</span>
                            </td>
                            <td class="figure">{{ line.amount | money }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              </div>

              <div>
                <div class="section-head">
                  <h2>In the books, not at the bank</h2>
                </div>
                @if (found.unreconciledLedgerLines.length === 0) {
                  <p class="note">Every journal line on this account is matched.</p>
                } @else {
                  <div class="scroll">
                    <table class="ledger">
                      <thead>
                        <tr>
                          @if (canMatch) {
                            <th scope="col"><span class="visually-hidden">Choose</span></th>
                          }
                          <th scope="col">Date</th>
                          <th scope="col" class="figure">Amount</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (
                          line of found.unreconciledLedgerLines;
                          track line.journalEntryId + ':' + line.lineNumber
                        ) {
                          <tr>
                            @if (canMatch) {
                              <td class="pick">
                                <input
                                  type="radio"
                                  name="ledgerLine"
                                  [attr.aria-label]="
                                    'Journal line dated ' + line.entryDate + ' for ' + line.amount
                                  "
                                  [checked]="
                                    ledgerKey() === line.journalEntryId + ':' + line.lineNumber
                                  "
                                  (change)="
                                    ledgerKey.set(line.journalEntryId + ':' + line.lineNumber)
                                  "
                                />
                              </td>
                            }
                            <td class="code">{{ line.entryDate }}</td>
                            <td class="figure">{{ line.amount | money }}</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                }
              </div>
            </div>

            @if (canMatch) {
              <div class="actions">
                <button
                  type="button"
                  class="button primary"
                  [disabled]="!canSubmitMatch()"
                  (click)="match()"
                >
                  Match the two chosen lines
                </button>
                @if (matchHint(); as text) {
                  <span class="muted">{{ text }}</span>
                }
              </div>
            }
          } @else {
            <p class="muted">Loading</p>
          }

          @if (error()) {
            <p class="note error" role="alert">{{ error() }}</p>
          }

          @if (canUpload) {
            <form class="inline-form" (ngSubmit)="upload()" novalidate>
              <h3>Import a statement</h3>
              <div class="controls">
                <label class="field">
                  <span>CSV file</span>
                  <input type="file" accept=".csv,text/csv" (change)="pick($event)" />
                </label>
                <button type="submit" class="button primary" [disabled]="!file() || uploading()">
                  {{ uploading() ? 'Importing' : 'Import statement' }}
                </button>
              </div>
              <p class="muted">Columns: date, reference, description, amount. One header row.</p>
              @if (imported(); as statement) {
                @if (statement.status === 'Processed') {
                  <p class="note done" role="status">
                    {{ statement.lineCount }} lines read, {{ statement.newLineCount }} new,
                    {{ statement.matchedCount }} matched.
                  </p>
                } @else if (statement.status === 'Failed') {
                  <p class="note error" role="alert">{{ statement.error }}</p>
                } @else {
                  <p class="note" role="status">Accepted. Waiting for the worker to process it.</p>
                }
              }
            </form>
          }

          @if (statements().value; as recent) {
            @if (recent.length > 0) {
              <div>
                <div class="section-head">
                  <h2>Recent imports</h2>
                </div>
                <div class="scroll">
                  <table class="ledger">
                    <thead>
                      <tr>
                        <th scope="col">Imported</th>
                        <th scope="col">By</th>
                        <th scope="col">Status</th>
                        <th scope="col" class="figure">Lines</th>
                        <th scope="col" class="figure">New</th>
                        <th scope="col" class="figure">Matched</th>
                      </tr>
                    </thead>
                    <tbody>
                      @for (statement of recent; track statement.id) {
                        <tr>
                          <td class="code">
                            {{ statement.importedAt | date: 'yyyy-MM-dd HH:mm' }}
                          </td>
                          <td>{{ statement.importedBy }}</td>
                          <td>
                            <span class="status" [class]="statement.status.toLowerCase()">{{
                              statement.status
                            }}</span>
                          </td>
                          <td class="figure">{{ statement.lineCount }}</td>
                          <td class="figure">{{ statement.newLineCount }}</td>
                          <td class="figure">{{ statement.matchedCount }}</td>
                        </tr>
                      }
                    </tbody>
                  </table>
                </div>
              </div>
            }
          }
        </div>
      }
    </section>
  `,
  styles: `
    select {
      min-width: 14rem;
    }

    .pick {
      width: 1%;
    }

    .pick input {
      height: auto;
      accent-color: var(--accent);
    }

    input[type='file'] {
      height: auto;
      padding: var(--space-1);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-4);
    }
  `,
})
export class ReconciliationPage {
  private readonly api = inject(Api);
  private readonly destroyRef = inject(DestroyRef);
  private readonly auth = inject(Auth);
  private readonly accounts = loaded<Account[]>();

  protected readonly canMatch = this.auth.has('Approver');
  protected readonly canUpload = this.auth.has('Clerk');
  protected readonly accountId = signal('');
  protected readonly asOf = signal(today());
  protected readonly report = loaded<Reconciliation>();
  protected readonly statements = loaded<BankStatement[]>();
  protected readonly bankLineId = signal('');
  protected readonly ledgerKey = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly uploading = signal(false);
  protected readonly imported = signal<BankStatement | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected readonly banks = computed(() =>
    (this.accounts().value ?? []).filter((account) => account.type === 'Asset' && account.isActive),
  );

  private readonly chosen = computed(() => {
    const found = this.report().value;
    const bank = found?.unmatchedBankLines.find((line) => line.id === this.bankLineId());
    const ledger = found?.unreconciledLedgerLines.find(
      (line) => `${line.journalEntryId}:${line.lineNumber}` === this.ledgerKey(),
    );
    return { bank, ledger };
  });

  protected readonly matchHint = computed(() => {
    const { bank, ledger } = this.chosen();
    if (!bank || !ledger) {
      return 'Choose one line on each side.';
    }

    return bank.amount === ledger.amount ? null : 'The two amounts are not equal.';
  });
  protected readonly canSubmitMatch = computed(() => !this.busy() && this.matchHint() === null);

  constructor() {
    load(this.api.accounts(), this.accounts);
  }

  protected choose(accountId: string): void {
    this.accountId.set(accountId);
    this.imported.set(null);
    this.refresh();
  }

  protected refresh(): void {
    this.bankLineId.set('');
    this.ledgerKey.set('');
    this.error.set('');

    if (this.accountId() && this.asOf()) {
      load(this.api.reconciliation(this.accountId(), this.asOf()), this.report);
      load(this.api.statements(this.accountId()), this.statements);
    }
  }

  protected pick(event: Event): void {
    this.file.set((event.target as HTMLInputElement).files?.[0] ?? null);
  }

  protected match(): void {
    const { bank, ledger } = this.chosen();
    if (!bank || !ledger) {
      return;
    }

    this.busy.set(true);
    this.api.matchBankLine(bank.id, ledger.journalEntryId, ledger.lineNumber).subscribe({
      next: () => {
        this.busy.set(false);
        this.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(toProblem(error).detail);
      },
    });
  }

  protected async upload(): Promise<void> {
    const file = this.file();
    if (!file) {
      return;
    }

    this.uploading.set(true);
    this.error.set('');
    const csv = await file.text();

    this.api.uploadStatement(this.accountId(), csv).subscribe({
      next: (statement) => {
        this.imported.set(statement);
        this.follow(statement.id);
      },
      error: (error: unknown) => {
        this.uploading.set(false);
        this.error.set(toProblem(error).detail);
      },
    });
  }

  private follow(statementId: string): void {
    timer(0, 1000)
      .pipe(
        take(30),
        switchMap(() => this.api.bankStatement(statementId)),
        takeWhile(
          (statement) => statement.status === 'Pending' || statement.status === 'Processing',
          true,
        ),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe({
        next: (statement) => this.imported.set(statement),
        error: (error: unknown) => {
          this.uploading.set(false);
          this.error.set(toProblem(error).detail);
        },
        complete: () => {
          this.uploading.set(false);
          this.refresh();
        },
      });
  }
}
