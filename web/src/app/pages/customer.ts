import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Account, Api, Customer, Payment, Receivables, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';
import { summarisePayment } from './invoice.model';

@Component({
  selector: 'app-customer',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <p><a routerLink="/receivables/customers">Customers</a></p>
          <h1>{{ customer()?.name ?? 'Customer' }}</h1>
          @if (customer(); as found) {
            <p class="code">{{ found.code }}</p>
          }
        </div>
        @if (receivables().value; as owed) {
          <dl class="figures">
            <div>
              <dt>Outstanding</dt>
              <dd>{{ owed.outstanding | money }}</dd>
            </div>
          </dl>
        }
      </header>

      @if (receivables().state === 'error') {
        <p class="note error" role="alert">{{ receivables().problem?.detail }}</p>
      } @else if (receivables().value; as owed) {
        <form class="stack" (ngSubmit)="receive()" novalidate>
          @if (owed.invoices.length === 0) {
            <p class="note">This customer has no open invoices.</p>
          } @else {
            <div class="scroll">
              <table class="ledger" [class.plain]="canReceive">
                <thead>
                  <tr>
                    <th scope="col">Invoice</th>
                    <th scope="col" class="wide-only">Issued</th>
                    <th scope="col">Due</th>
                    <th scope="col" class="figure wide-only">Total</th>
                    <th scope="col" class="figure">Outstanding</th>
                    @if (canReceive) {
                      <th scope="col" class="figure amount">Paying now</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (invoice of owed.invoices; track invoice.id) {
                    <tr>
                      <td>
                        <a [routerLink]="['/receivables/invoices', invoice.id]"
                          >Invoice {{ invoice.number }}</a
                        >
                      </td>
                      <td class="code wide-only">{{ invoice.issueDate }}</td>
                      <td class="code">{{ invoice.dueDate }}</td>
                      <td class="figure wide-only">{{ invoice.total | money }}</td>
                      <td class="figure">{{ invoice.outstanding | money }}</td>
                      @if (canReceive) {
                        <td class="amount">
                          <input
                            class="figure"
                            inputmode="decimal"
                            [name]="'paying' + $index"
                            [attr.aria-label]="'Amount for invoice ' + invoice.number"
                            [ngModel]="amounts()[invoice.id]"
                            (ngModelChange)="allocate(invoice.id, $event)"
                          />
                        </td>
                      }
                    </tr>
                  }
                </tbody>
                <tfoot>
                  <tr>
                    <td>Total</td>
                    <td class="wide-only"></td>
                    <td></td>
                    <td class="wide-only"></td>
                    <td class="figure">{{ owed.outstanding | money }}</td>
                    @if (canReceive) {
                      <td class="figure">{{ summary().total | money }}</td>
                    }
                  </tr>
                </tfoot>
              </table>
            </div>

            @if (canReceive) {
              <div class="controls">
                <label class="field">
                  <span>Payment date</span>
                  <input type="date" name="paymentDate" [(ngModel)]="paymentDate" />
                </label>
                <label class="field">
                  <span>Deposited to</span>
                  <select name="deposit" [(ngModel)]="depositAccountId">
                    <option value="">Choose an account</option>
                    @for (account of deposits(); track account.id) {
                      <option [value]="account.id">{{ account.code }} {{ account.name }}</option>
                    }
                  </select>
                </label>
                <button type="submit" class="button primary" [disabled]="!canSubmit()">
                  Receive payment
                </button>
                <button type="button" class="button quiet" (click)="payInFull(owed)">
                  Pay everything
                </button>
              </div>
              @if (hint(); as text) {
                <p class="muted">{{ text }}</p>
              }
            }
          }

          @if (error()) {
            <p class="note error" role="alert">{{ error() }}</p>
          }
          @if (received(); as payment) {
            <p class="note done" role="status">
              Payment of {{ payment.amount | money }} received by {{ payment.receivedBy }} on
              {{ payment.paymentDate }}.
            </p>
          }
        </form>
      } @else {
        <p class="muted">Loading</p>
      }
    </section>
  `,
  styles: `
    .amount {
      width: 9rem;
    }

    select {
      min-width: 14rem;
    }
  `,
})
export class CustomerPage {
  private readonly api = inject(Api);
  private readonly customers = loaded<Customer[]>();
  private readonly accounts = loaded<Account[]>();
  private idempotencyKey = crypto.randomUUID();

  readonly id = input.required<string>();

  protected readonly canReceive = inject(Auth).has('Clerk');
  protected readonly receivables = loaded<Receivables>();
  protected readonly amounts = signal<Record<string, string>>({});
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly received = signal<Payment | null>(null);

  protected paymentDate = today();
  protected depositAccountId = '';

  protected readonly customer = computed(
    () => this.customers().value?.find((customer) => customer.id === this.id()) ?? null,
  );
  protected readonly deposits = computed(() =>
    (this.accounts().value ?? []).filter((account) => account.type === 'Asset' && account.isActive),
  );
  protected readonly summary = computed(() =>
    summarisePayment(
      (this.receivables().value?.invoices ?? []).map((invoice) => ({
        invoiceId: invoice.id,
        outstanding: invoice.outstanding,
        amount: this.amounts()[invoice.id] ?? '',
      })),
    ),
  );
  protected readonly canSubmit = computed(() => !this.busy() && this.summary().problem === null);
  protected readonly hint = computed(() =>
    Object.values(this.amounts()).some((amount) => amount.trim()) ? this.summary().problem : null,
  );

  constructor() {
    load(this.api.customers(), this.customers);
    load(this.api.accounts(), this.accounts);
    effect(() => load(this.api.receivables(this.id()), this.receivables));
  }

  protected allocate(invoiceId: string, amount: string): void {
    this.amounts.update((amounts) => ({ ...amounts, [invoiceId]: amount }));
    this.received.set(null);
  }

  protected payInFull(owed: Receivables): void {
    this.amounts.set(
      Object.fromEntries(
        owed.invoices.map((invoice) => [invoice.id, invoice.outstanding.toFixed(2)]),
      ),
    );
  }

  protected receive(): void {
    const summary = this.summary();
    const owed = this.receivables().value;
    if (summary.problem || !owed) {
      return;
    }

    if (!this.depositAccountId) {
      this.error.set('Choose the account the money was deposited to.');
      return;
    }

    this.busy.set(true);
    this.error.set('');

    this.api
      .receivePayment(
        {
          customerId: this.id(),
          paymentDate: this.paymentDate,
          amount: summary.total,
          currency: owed.invoices[0].currency,
          depositAccountId: this.depositAccountId,
          allocations: summary.allocations,
        },
        this.idempotencyKey,
      )
      .subscribe({
        next: (payment) => {
          this.busy.set(false);
          this.received.set(payment);
          this.amounts.set({});
          this.idempotencyKey = crypto.randomUUID();
          load(this.api.receivables(this.id()), this.receivables);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(toProblem(error).detail);
        },
      });
  }
}
