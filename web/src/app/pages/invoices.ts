import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, Customer, Invoice, InvoiceStatus } from '../core/api';
import { Auth } from '../core/auth';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

@Component({
  selector: 'app-invoices',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Invoices</h1>
          <p>Drafts first, then issued invoices from the newest number down.</p>
        </div>
        <div class="controls">
          <label class="field">
            <span>Status</span>
            <select [ngModel]="status()" (ngModelChange)="filter($event)">
              <option value="">All</option>
              @for (option of statuses; track option) {
                <option [value]="option">{{ option }}</option>
              }
            </select>
          </label>
          @if (canDraft) {
            <a class="button primary" routerLink="/receivables/invoices/new">New invoice</a>
          }
        </div>
      </header>

      @if (invoices().state === 'error') {
        <p class="note error" role="alert">{{ invoices().problem?.detail }}</p>
      } @else if (invoices().value; as list) {
        @if (list.length === 0) {
          <p class="note">No invoices match.</p>
        } @else {
          <div class="scroll">
            <table class="ledger">
              <thead>
                <tr>
                  <th scope="col">Invoice</th>
                  <th scope="col">Customer</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="wide-only">Issued</th>
                  <th scope="col" class="wide-only">Due</th>
                  <th scope="col" class="figure">Total</th>
                  <th scope="col" class="figure wide-only">Outstanding</th>
                </tr>
              </thead>
              <tbody>
                @for (invoice of list; track invoice.id) {
                  <tr>
                    <td>
                      <a [routerLink]="['/receivables/invoices', invoice.id]">{{
                        invoice.number === null ? 'Draft' : 'Invoice ' + invoice.number
                      }}</a>
                    </td>
                    <td>{{ names()[invoice.customerId] }}</td>
                    <td>
                      <span class="status" [class]="invoice.status.toLowerCase()">{{
                        invoice.status
                      }}</span>
                    </td>
                    <td class="code wide-only">{{ invoice.issueDate }}</td>
                    <td class="code wide-only">{{ invoice.dueDate }}</td>
                    <td class="figure">{{ invoice.total | money }}</td>
                    <td class="figure wide-only">{{ invoice.outstanding | money: true }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      } @else {
        <p class="muted">Loading</p>
      }
    </section>
  `,
})
export class InvoicesPage {
  private readonly api = inject(Api);
  private readonly customers = loaded<Customer[]>();

  protected readonly canDraft = inject(Auth).has('Clerk');
  protected readonly statuses: InvoiceStatus[] = ['Draft', 'Issued', 'Paid', 'Void'];
  protected readonly status = signal<InvoiceStatus | ''>('');
  protected readonly invoices = loaded<Invoice[]>();
  protected readonly names = computed(() =>
    Object.fromEntries(
      (this.customers().value ?? []).map((customer) => [customer.id, customer.name]),
    ),
  );

  constructor() {
    load(this.api.customers(), this.customers);
    this.refresh();
  }

  protected filter(status: InvoiceStatus | ''): void {
    this.status.set(status);
    this.refresh();
  }

  private refresh(): void {
    load(this.api.invoices(this.status()), this.invoices);
  }
}
