import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, TrialBalance } from '../core/api';
import { today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

@Component({
  selector: 'app-trial-balance',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Trial balance</h1>
          <p>Account balances as of a date.</p>
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
      } @else if (report().value; as balance) {
        @if (balance.rows.length === 0) {
          <p class="note">Nothing has been posted on or before this date.</p>
        } @else {
          <div class="scroll">
            <table class="ledger">
              <thead>
                <tr>
                  <th scope="col">Code</th>
                  <th scope="col">Account</th>
                  <th scope="col" class="wide-only">Type</th>
                  <th scope="col" class="figure">Debit</th>
                  <th scope="col" class="figure">Credit</th>
                </tr>
              </thead>
              <tbody>
                @for (row of balance.rows; track row.accountId) {
                  <tr>
                    <td class="code">{{ row.code }}</td>
                    <td>
                      <a [routerLink]="['/accounts', row.accountId]">{{ row.name }}</a>
                    </td>
                    <td class="muted wide-only">{{ row.type }}</td>
                    <td class="figure">{{ row.debit | money: true }}</td>
                    <td class="figure">{{ row.credit | money: true }}</td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr>
                  <td></td>
                  <td>
                    <span class="status" [class.good]="balanced()" [class.bad]="!balanced()">
                      {{ balanced() ? 'In balance' : 'Out of balance' }}
                    </span>
                  </td>
                  <td class="wide-only"></td>
                  <td class="figure">{{ balance.totalDebit | money }}</td>
                  <td class="figure">{{ balance.totalCredit | money }}</td>
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
export class TrialBalancePage {
  private readonly api = inject(Api);

  protected readonly asOf = signal(today());
  protected readonly report = loaded<TrialBalance>();
  protected readonly balanced = computed(() => {
    const balance = this.report().value;
    return balance !== null && balance.totalDebit === balance.totalCredit;
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
    load(this.api.trialBalance(this.asOf()), this.report);
  }
}
