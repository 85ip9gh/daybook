import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Account, Api, Customer, Invoice, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

@Component({
  selector: 'app-invoice',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      @if (invoice().state === 'error') {
        <p class="note error" role="alert">{{ invoice().problem?.detail }}</p>
      } @else if (invoice().value; as found) {
        <header class="page-head">
          <div>
            <p><a routerLink="/receivables/invoices">Invoices</a></p>
            <h1>{{ found.number === null ? 'Draft invoice' : 'Invoice ' + found.number }}</h1>
            <p>
              <a [routerLink]="['/receivables/customers', found.customerId]">{{
                customer()?.name ?? 'Customer'
              }}</a>
            </p>
          </div>
          <dl class="figures">
            <div>
              <dt>Status</dt>
              <dd>
                <span class="status" [class]="found.status.toLowerCase()">{{ found.status }}</span>
              </dd>
            </div>
            <div>
              <dt>Total</dt>
              <dd>{{ found.total | money }}</dd>
            </div>
            <div>
              <dt>Outstanding</dt>
              <dd>{{ found.outstanding | money }}</dd>
            </div>
          </dl>
        </header>

        <div class="stack">
          <dl class="facts">
            <div>
              <dt>Issued</dt>
              <dd class="code">{{ found.issueDate ?? 'Not issued' }}</dd>
            </div>
            <div>
              <dt>Due</dt>
              <dd class="code">{{ found.dueDate ?? 'Not issued' }}</dd>
            </div>
            <div>
              <dt>Drafted by</dt>
              <dd>{{ found.draftedBy }}</dd>
            </div>
            @if (found.issuedBy) {
              <div>
                <dt>Issued by</dt>
                <dd>{{ found.issuedBy }}</dd>
              </div>
            }
            @if (found.voidedBy) {
              <div>
                <dt>Voided by</dt>
                <dd>{{ found.voidedBy }}</dd>
              </div>
            }
          </dl>

          <div class="scroll">
            <table class="ledger">
              <thead>
                <tr>
                  <th scope="col">Description</th>
                  <th scope="col" class="figure wide-only">Quantity</th>
                  <th scope="col" class="figure wide-only">Unit price</th>
                  <th scope="col" class="wide-only">Revenue account</th>
                  <th scope="col" class="figure">Amount</th>
                </tr>
              </thead>
              <tbody>
                @for (line of found.lines; track line.lineNumber) {
                  <tr>
                    <td>{{ line.description }}</td>
                    <td class="figure wide-only">{{ line.quantity }}</td>
                    <td class="figure wide-only">{{ line.unitPrice | money }}</td>
                    <td class="wide-only muted">{{ accountNames()[line.revenueAccountId] }}</td>
                    <td class="figure">{{ line.amount | money }}</td>
                  </tr>
                }
                <tr class="carried">
                  <td>Subtotal</td>
                  <td class="wide-only" colspan="3"></td>
                  <td class="figure">{{ found.subtotal | money }}</td>
                </tr>
                <tr class="carried">
                  <td>Tax</td>
                  <td class="wide-only" colspan="3"></td>
                  <td class="figure">{{ found.tax | money }}</td>
                </tr>
              </tbody>
              <tfoot>
                <tr>
                  <td>Total</td>
                  <td class="wide-only" colspan="3"></td>
                  <td class="figure">{{ found.total | money }}</td>
                </tr>
              </tfoot>
            </table>
          </div>

          @if (canPost && found.status === 'Draft') {
            <form class="inline-form" (ngSubmit)="issue()" novalidate>
              <h3>Issue this invoice</h3>
              <div class="controls">
                <label class="field">
                  <span>Issue date</span>
                  <input type="date" name="issueDate" [(ngModel)]="issueDate" />
                </label>
                <label class="field">
                  <span>Due date</span>
                  <input type="date" name="dueDate" [(ngModel)]="dueDate" />
                </label>
                <button type="submit" class="button primary" [disabled]="busy()">
                  Issue invoice
                </button>
              </div>
              <p class="muted">Numbers the invoice and posts it.</p>
            </form>
          }

          @if (canPost && found.status === 'Issued' && found.amountPaid === 0) {
            <form class="inline-form" (ngSubmit)="confirming.set(true)" novalidate>
              <h3>Void this invoice</h3>
              <div class="controls">
                <label class="field">
                  <span>Void date</span>
                  <input type="date" name="voidDate" [(ngModel)]="voidDate" />
                </label>
                @if (!confirming()) {
                  <button type="submit" class="button" [disabled]="busy()">Void invoice</button>
                } @else {
                  <button
                    type="button"
                    class="button primary"
                    [disabled]="busy()"
                    (click)="voidInvoice()"
                  >
                    Confirm, void it
                  </button>
                  <button type="button" class="button quiet" (click)="confirming.set(false)">
                    Cancel
                  </button>
                }
              </div>
              <p class="muted">Posts a reversing entry.</p>
            </form>
          }

          @if (error()) {
            <p class="note error" role="alert">{{ error() }}</p>
          }
        </div>
      } @else {
        <p class="muted">Loading</p>
      }
    </section>
  `,
})
export class InvoicePage {
  private readonly api = inject(Api);
  private readonly customers = loaded<Customer[]>();
  private readonly accounts = loaded<Account[]>();

  readonly id = input.required<string>();

  protected readonly canPost = inject(Auth).has('Approver');
  protected readonly invoice = loaded<Invoice>();
  protected readonly busy = signal(false);
  protected readonly confirming = signal(false);
  protected readonly error = signal('');

  protected issueDate = today();
  protected dueDate = today();
  protected voidDate = today();

  protected readonly customer = computed(
    () =>
      this.customers().value?.find(
        (customer) => customer.id === this.invoice().value?.customerId,
      ) ?? null,
  );
  protected readonly accountNames = computed(() =>
    Object.fromEntries(
      (this.accounts().value ?? []).map((account) => [
        account.id,
        `${account.code} ${account.name}`,
      ]),
    ),
  );

  constructor() {
    load(this.api.customers(), this.customers);
    load(this.api.accounts(), this.accounts);
    effect(() => load(this.api.invoice(this.id()), this.invoice));
  }

  protected issue(): void {
    this.run(this.api.issueInvoice(this.id(), this.issueDate, this.dueDate));
  }

  protected voidInvoice(): void {
    this.run(this.api.voidInvoice(this.id(), this.voidDate));
  }

  private run(action: ReturnType<Api['issueInvoice']>): void {
    this.busy.set(true);
    this.error.set('');

    action.subscribe({
      next: (invoice) => {
        this.busy.set(false);
        this.confirming.set(false);
        this.invoice.set({ state: 'ready', value: invoice, problem: null });
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.confirming.set(false);
        this.error.set(toProblem(error).detail);
      },
    });
  }
}
