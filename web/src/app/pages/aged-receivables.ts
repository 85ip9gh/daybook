import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { AgedCustomer, AgedReceivables, Api } from '../core/api';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

type Bucket = 'current' | 'days1To30' | 'days31To60' | 'days61To90' | 'over90';

@Component({
  selector: 'app-aged-receivables',
  imports: [FormsModule, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Aged receivables</h1>
          <p>What each customer owed on the date chosen, by how long it had been due.</p>
        </div>
        <div class="controls">
          <label class="field">
            <span>As of</span>
            <input type="date" [ngModel]="asOf()" (ngModelChange)="change($event)" />
          </label>
        </div>
      </header>

      @if (report().state === 'error') {
        <p class="note error" role="alert">{{ report().problem?.detail }}</p>
      } @else if (report().value; as aged) {
        @if (aged.customers.length === 0) {
          <p class="note">No customer owed anything on this date.</p>
        } @else {
          <div class="scroll">
            <table class="ledger">
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Customer</th>
                  <th scope="col" class="figure">Not yet due</th>
                  <th scope="col" class="figure">1 to 30</th>
                  <th scope="col" class="figure">31 to 60</th>
                  <th scope="col" class="figure">61 to 90</th>
                  <th scope="col" class="figure">Over 90</th>
                  <th scope="col" class="figure">Total</th>
                </tr>
              </thead>
              <tbody>
                @for (customer of aged.customers; track customer.customerId) {
                  <tr>
                    <td class="code">{{ customer.code }}</td>
                    <td>{{ customer.name }}</td>
                    @for (bucket of buckets; track bucket) {
                      <td class="figure">{{ customer[bucket] | money: true }}</td>
                    }
                    <td class="figure">{{ customer.total | money }}</td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr>
                  <td colspan="2">Total</td>
                  @for (bucket of buckets; track bucket) {
                    <td class="figure">{{ totals()[bucket] | money: true }}</td>
                  }
                  <td class="figure">{{ aged.total | money }}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        }
      } @else {
        <p class="muted">Loading</p>
      }
    </section>
  `,
})
export class AgedReceivablesPage {
  private readonly api = inject(Api);

  protected readonly buckets: Bucket[] = [
    'current',
    'days1To30',
    'days31To60',
    'days61To90',
    'over90',
  ];
  protected readonly asOf = signal(today());
  protected readonly report = loaded<AgedReceivables>();
  protected readonly totals = computed(() => {
    const customers: AgedCustomer[] = this.report().value?.customers ?? [];
    const sum = (bucket: Bucket) =>
      Math.round(customers.reduce((total, customer) => total + customer[bucket] * 100, 0)) / 100;

    return {
      current: sum('current'),
      days1To30: sum('days1To30'),
      days31To60: sum('days31To60'),
      days61To90: sum('days61To90'),
      over90: sum('over90'),
    };
  });

  constructor() {
    this.refresh();
  }

  protected change(asOf: string): void {
    if (asOf) {
      this.asOf.set(asOf);
      this.refresh();
    }
  }

  private refresh(): void {
    load(this.api.agedReceivables(this.asOf()), this.report);
  }
}
