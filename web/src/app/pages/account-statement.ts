import { Component, computed, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Account, AccountStatement, Api } from '../core/api';
import { firstOfMonth, today } from '../core/dates';
import { load, loaded } from '../core/loaded';
import { MoneyPipe } from '../core/money';

@Component({
  selector: 'app-account-statement',
  imports: [FormsModule, RouterLink, MoneyPipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <p><a routerLink="/accounts">Accounts</a></p>
          <h1>{{ account()?.name ?? 'Account' }}</h1>
          @if (account(); as found) {
            <p>
              <span class="code">{{ found.code }}</span> · {{ found.type }} · {{ found.currency }}
            </p>
          }
        </div>
        <div class="controls">
          <label class="field">
            <span>From</span>
            <input type="date" [ngModel]="from()" (ngModelChange)="from.set($event)" />
          </label>
          <label class="field">
            <span>To</span>
            <input type="date" [ngModel]="to()" (ngModelChange)="to.set($event)" />
          </label>
        </div>
      </header>

      @if (statement().state === 'error') {
        <p class="note error" role="alert">{{ statement().problem?.detail }}</p>
      } @else if (statement().value; as found) {
        <div class="stack">
          @if (found.truncated) {
            <p class="note">
              Only the first 1,000 lines are shown. Narrow the dates to see the rest.
            </p>
          }
          <div class="scroll">
            <table class="ledger">
              <thead>
                <tr>
                  <th scope="col">Date</th>
                  <th scope="col">Description</th>
                  <th scope="col" class="figure">Debit</th>
                  <th scope="col" class="figure">Credit</th>
                  <th scope="col" class="figure">Balance</th>
                </tr>
              </thead>
              <tbody>
                <tr class="carried">
                  <td class="code">{{ found.from }}</td>
                  <td>Opening balance</td>
                  <td></td>
                  <td></td>
                  <td class="figure">{{ found.openingBalance | money }}</td>
                </tr>
                @for (line of found.lines; track line.journalEntryId + ':' + line.lineNumber) {
                  <tr>
                    <td class="code">{{ line.entryDate }}</td>
                    <td>{{ line.description }}</td>
                    <td class="figure">{{ line.side === 'Debit' ? (line.amount | money) : '' }}</td>
                    <td class="figure">
                      {{ line.side === 'Credit' ? (line.amount | money) : '' }}
                    </td>
                    <td class="figure">{{ line.balance | money }}</td>
                  </tr>
                }
              </tbody>
              <tfoot>
                <tr>
                  <td class="code">{{ found.to }}</td>
                  <td>Closing balance</td>
                  <td></td>
                  <td></td>
                  <td class="figure">{{ found.closingBalance | money }}</td>
                </tr>
              </tfoot>
            </table>
          </div>
        </div>
      } @else {
        <p class="muted">Loading</p>
      }
    </section>
  `,
  styles: `
    .code {
      font-family: var(--font-figure);
      font-size: var(--text-sm);
    }

    tfoot .code {
      font-weight: 400;
    }
  `,
})
export class AccountStatementPage {
  private readonly api = inject(Api);

  readonly id = input.required<string>();

  protected readonly to = signal(today());
  protected readonly from = signal(firstOfMonth(today()));
  protected readonly statement = loaded<AccountStatement>();
  protected readonly accounts = loaded<Account[]>();
  protected readonly account = computed(
    () => this.accounts().value?.find((account) => account.id === this.id()) ?? null,
  );

  constructor() {
    load(this.api.accounts(), this.accounts);

    effect(() => {
      const id = this.id();
      const from = this.from();
      const to = this.to();
      if (from && to) {
        load(this.api.statement(id, from, to), this.statement);
      }
    });
  }
}
