import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Account, Api, Customer, toProblem } from '../core/api';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';
import { DraftInvoiceLine, emptyInvoiceLine, summariseInvoice } from './invoice.model';

@Component({
  selector: 'app-invoice-new',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <p><a routerLink="/receivables/invoices">Invoices</a></p>
          <h1>New invoice</h1>
          <p>A draft posts nothing. Someone else has to issue it.</p>
        </div>
      </header>

      <form class="stack" (ngSubmit)="save()" novalidate>
        <div class="controls">
          <label class="field">
            <span>Customer</span>
            <select name="customer" [(ngModel)]="customerId">
              <option value="">Choose a customer</option>
              @for (customer of customers().value ?? []; track customer.id) {
                <option [value]="customer.id">{{ customer.code }} {{ customer.name }}</option>
              }
            </select>
          </label>
          <label class="field">
            <span>Receivable account</span>
            <select name="receivable" [(ngModel)]="receivableAccountId">
              <option value="">Choose an account</option>
              @for (account of ofType('Asset'); track account.id) {
                <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
              }
            </select>
          </label>
        </div>

        <div class="scroll">
          <table class="ledger plain">
            <thead>
              <tr>
                <th scope="col">Description</th>
                <th scope="col" class="figure quantity">Quantity</th>
                <th scope="col" class="figure price">Unit price</th>
                <th scope="col">Revenue account</th>
                <th scope="col" class="figure">Amount</th>
                <th scope="col"><span class="visually-hidden">Remove</span></th>
              </tr>
            </thead>
            <tbody>
              @for (line of lines(); track $index) {
                <tr>
                  <td>
                    <input
                      class="description"
                      maxlength="200"
                      [name]="'description' + $index"
                      [attr.aria-label]="'Description for line ' + ($index + 1)"
                      [ngModel]="line.description"
                      (ngModelChange)="edit($index, { description: $event })"
                    />
                  </td>
                  <td class="quantity">
                    <input
                      class="figure"
                      inputmode="decimal"
                      [name]="'quantity' + $index"
                      [attr.aria-label]="'Quantity for line ' + ($index + 1)"
                      [ngModel]="line.quantity"
                      (ngModelChange)="edit($index, { quantity: $event })"
                    />
                  </td>
                  <td class="price">
                    <input
                      class="figure"
                      inputmode="decimal"
                      [name]="'price' + $index"
                      [attr.aria-label]="'Unit price for line ' + ($index + 1)"
                      [ngModel]="line.unitPrice"
                      (ngModelChange)="edit($index, { unitPrice: $event })"
                    />
                  </td>
                  <td>
                    <select
                      [name]="'revenue' + $index"
                      [attr.aria-label]="'Revenue account for line ' + ($index + 1)"
                      [ngModel]="line.revenueAccountId"
                      (ngModelChange)="edit($index, { revenueAccountId: $event })"
                    >
                      <option value="">Choose an account</option>
                      @for (account of ofType('Revenue'); track account.id) {
                        <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
                      }
                    </select>
                  </td>
                  <td class="figure">{{ summary().amounts[$index] | money }}</td>
                  <td class="remove">
                    <button
                      type="button"
                      class="button quiet"
                      [disabled]="lines().length <= 1"
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
                <td colspan="4">
                  <button type="button" class="button quiet" (click)="add()">Add a line</button>
                </td>
                <td class="figure">{{ summary().subtotal | money }}</td>
                <td></td>
              </tr>
            </tfoot>
          </table>
        </div>

        <div class="controls">
          <label class="field rate">
            <span>Tax, percent</span>
            <input
              class="figure"
              name="taxPercent"
              inputmode="decimal"
              [ngModel]="taxPercent()"
              (ngModelChange)="taxPercent.set($event)"
            />
          </label>
          <label class="field">
            <span>Tax owed on</span>
            <select
              name="taxAccount"
              [ngModel]="taxAccountId()"
              (ngModelChange)="taxAccountId.set($event)"
            >
              <option value="">No tax</option>
              @for (account of ofType('Liability'); track account.id) {
                <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
              }
            </select>
          </label>
          <dl class="figures">
            <div>
              <dt>Subtotal</dt>
              <dd>{{ summary().subtotal | money }}</dd>
            </div>
            <div>
              <dt>Tax</dt>
              <dd>{{ summary().tax | money }}</dd>
            </div>
            <div>
              <dt>Total</dt>
              <dd>{{ summary().total | money }}</dd>
            </div>
          </dl>
        </div>

        <div class="actions">
          <button type="submit" class="button primary" [disabled]="!canSave()">Save draft</button>
          @if (hint(); as text) {
            <span class="muted">{{ text }}</span>
          }
        </div>

        @if (error()) {
          <p class="note error" role="alert">{{ error() }}</p>
        }
      </form>
    </section>
  `,
  styles: `
    select {
      min-width: 13rem;
      width: 100%;
    }

    .description {
      width: 100%;
      min-width: 12rem;
    }

    .quantity {
      width: 6.5rem;
    }

    .price {
      width: 8rem;
    }

    .rate input {
      width: 6rem;
    }

    .remove {
      width: 1%;
    }

    tfoot .button {
      margin-left: calc(var(--space-2) * -1);
    }

    .figures {
      margin-left: auto;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-4);
    }
  `,
})
export class InvoiceNewPage {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly accounts = loaded<Account[]>();

  protected readonly customers = loaded<Customer[]>();
  protected readonly lines = signal<DraftInvoiceLine[]>([emptyInvoiceLine()]);
  protected readonly taxPercent = signal('');
  protected readonly taxAccountId = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected customerId = '';
  protected receivableAccountId = '';

  protected readonly summary = computed(() =>
    summariseInvoice(this.lines(), this.taxPercent(), this.taxAccountId()),
  );
  protected readonly canSave = computed(() => !this.busy() && this.summary().problem === null);
  protected readonly hint = computed(() => {
    const touched = this.lines().some(
      (line) => line.description || line.quantity || line.unitPrice || line.revenueAccountId,
    );
    return touched ? this.summary().problem : null;
  });

  constructor() {
    load(this.api.customers(), this.customers);
    load(this.api.accounts(), this.accounts);
  }

  protected ofType(type: Account['type']): Account[] {
    return (this.accounts().value ?? []).filter(
      (account) => account.type === type && account.isActive,
    );
  }

  protected edit(index: number, change: Partial<DraftInvoiceLine>): void {
    this.lines.update((lines) =>
      lines.map((line, position) => (position === index ? { ...line, ...change } : line)),
    );
  }

  protected add(): void {
    this.lines.update((lines) => [...lines, emptyInvoiceLine()]);
  }

  protected remove(index: number): void {
    this.lines.update((lines) => lines.filter((_, position) => position !== index));
  }

  protected save(): void {
    const summary = this.summary();
    if (summary.problem) {
      return;
    }

    if (!this.customerId || !this.receivableAccountId) {
      this.error.set('Choose a customer and a receivable account.');
      return;
    }

    const receivable = this.ofType('Asset').find(
      (account) => account.id === this.receivableAccountId,
    );
    this.busy.set(true);
    this.error.set('');

    this.api
      .draftInvoice({
        customerId: this.customerId,
        currency: receivable?.currency ?? 'CAD',
        receivableAccountId: this.receivableAccountId,
        lines: summary.lines,
        tax: summary.rate === null ? null : { rate: summary.rate, accountId: this.taxAccountId() },
      })
      .subscribe({
        next: (invoice) => void this.router.navigate(['/receivables/invoices', invoice.id]),
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(toProblem(error).detail);
        },
      });
  }
}
