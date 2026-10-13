import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Account, Api, Entry, toProblem } from '../core/api';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';
import { DraftLine, emptyLine, summarise } from './journal-entry.model';

@Component({
  selector: 'app-journal-entry',
  imports: [FormsModule, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>New journal entry</h1>
          <p>Debits must equal credits.</p>
        </div>
      </header>

      <form class="stack" (ngSubmit)="post()" novalidate>
        <div class="controls">
          <label class="field">
            <span>Date</span>
            <input type="date" name="entryDate" [(ngModel)]="entryDate" />
          </label>
          <label class="field description">
            <span>Description</span>
            <input name="description" maxlength="200" [(ngModel)]="description" />
          </label>
        </div>

        <div class="scroll">
          <table class="ledger plain">
            <thead>
              <tr>
                <th scope="col">Account</th>
                <th scope="col" class="figure amount">Debit</th>
                <th scope="col" class="figure amount">Credit</th>
                <th scope="col"><span class="visually-hidden">Remove</span></th>
              </tr>
            </thead>
            <tbody>
              @for (line of lines(); track $index) {
                <tr>
                  <td>
                    <select
                      [name]="'account' + $index"
                      [attr.aria-label]="'Account for line ' + ($index + 1)"
                      [ngModel]="line.accountId"
                      (ngModelChange)="edit($index, { accountId: $event })"
                    >
                      <option value="">Choose an account</option>
                      @for (account of active(); track account.id) {
                        <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
                      }
                    </select>
                  </td>
                  <td class="amount">
                    <input
                      class="figure"
                      inputmode="decimal"
                      [name]="'debit' + $index"
                      [attr.aria-label]="'Debit for line ' + ($index + 1)"
                      [ngModel]="line.debit"
                      (ngModelChange)="edit($index, { debit: $event })"
                    />
                  </td>
                  <td class="amount">
                    <input
                      class="figure"
                      inputmode="decimal"
                      [name]="'credit' + $index"
                      [attr.aria-label]="'Credit for line ' + ($index + 1)"
                      [ngModel]="line.credit"
                      (ngModelChange)="edit($index, { credit: $event })"
                    />
                  </td>
                  <td class="remove">
                    <button
                      type="button"
                      class="button quiet"
                      [disabled]="lines().length <= 2"
                      (click)="remove($index)"
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              }
            </tbody>
            <tfoot>
              <tr>
                <td>
                  <button type="button" class="button quiet" (click)="add()">Add a line</button>
                </td>
                <td class="figure">{{ summary().debit | money }}</td>
                <td class="figure">{{ summary().credit | money }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div class="actions">
          <button type="submit" class="button primary" [disabled]="!canPost()">Post entry</button>
          @if (hint(); as text) {
            <span class="muted">{{ text }}</span>
          }
        </div>

        @if (error()) {
          <p class="note error" role="alert">{{ error() }}</p>
        }

        @if (posted(); as entry) {
          <p class="note done" role="status">
            Posted by {{ entry.postedBy }} on {{ entry.entryDate }} with
            {{ entry.lines.length }} lines. Reference <span class="reference">{{ entry.id }}</span>
          </p>
        }
      </form>
    </section>
  `,
  styles: `
    .description {
      flex: 1 1 20rem;
    }

    .description input {
      width: 100%;
    }

    select {
      width: 100%;
      min-width: 14rem;
    }

    .amount {
      width: 10rem;
    }

    .remove {
      width: 1%;
      text-align: right;
    }

    tfoot .button {
      margin-left: calc(var(--space-2) * -1);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-4);
    }

    .reference {
      font-family: var(--font-figure);
      font-size: var(--text-sm);
    }
  `,
})
export class JournalEntryPage {
  private readonly api = inject(Api);
  private readonly accounts = loaded<Account[]>();
  private idempotencyKey = crypto.randomUUID();

  protected entryDate = today();
  protected description = '';
  protected readonly lines = signal<DraftLine[]>([emptyLine(), emptyLine()]);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly posted = signal<Entry | null>(null);

  protected readonly active = computed(() =>
    (this.accounts().value ?? []).filter((account) => account.isActive),
  );
  protected readonly summary = computed(() => summarise(this.lines()));
  protected readonly canPost = computed(() => !this.busy() && this.summary().problem === null);
  protected readonly hint = computed(() => {
    const touched = this.lines().some((line) => line.accountId || line.debit || line.credit);
    return touched ? this.summary().problem : null;
  });

  constructor() {
    load(this.api.accounts(), this.accounts);
  }

  protected edit(index: number, change: Partial<DraftLine>): void {
    this.lines.update((lines) =>
      lines.map((line, position) => (position === index ? { ...line, ...change } : line)),
    );
    this.posted.set(null);
  }

  protected add(): void {
    this.lines.update((lines) => [...lines, emptyLine()]);
  }

  protected remove(index: number): void {
    this.lines.update((lines) => lines.filter((_, position) => position !== index));
  }

  protected post(): void {
    const summary = this.summary();
    if (summary.problem) {
      return;
    }

    if (!this.description.trim()) {
      this.error.set('Give the entry a description.');
      return;
    }

    this.busy.set(true);
    this.error.set('');

    this.api
      .postEntry(
        {
          entryDate: this.entryDate,
          description: this.description.trim(),
          currency: this.currencyOf(summary.lines[0].accountId),
          lines: summary.lines,
        },
        this.idempotencyKey,
      )
      .subscribe({
        next: (entry) => {
          this.busy.set(false);
          this.posted.set(entry);
          this.description = '';
          this.lines.set([emptyLine(), emptyLine()]);
          this.idempotencyKey = crypto.randomUUID();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(toProblem(error).detail);
        },
      });
  }

  private currencyOf(accountId: string): string {
    return this.active().find((account) => account.id === accountId)?.currency ?? 'CAD';
  }
}
