import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Account, AccountType, Api, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { load, loaded } from '../core/loaded';

@Component({
  selector: 'app-accounts',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Accounts</h1>
          <p>Chart of accounts.</p>
        </div>
      </header>

      <div class="stack">
        @if (canOpen) {
          <form class="open" (ngSubmit)="open()" novalidate>
            <h3>Open an account</h3>
            <div class="controls">
              <label class="field code">
                <span>Code</span>
                <input name="code" maxlength="20" [(ngModel)]="code" />
              </label>
              <label class="field name">
                <span>Name</span>
                <input name="name" maxlength="100" [(ngModel)]="name" />
              </label>
              <label class="field">
                <span>Type</span>
                <select name="type" [(ngModel)]="type">
                  @for (option of types; track option) {
                    <option [value]="option">{{ option }}</option>
                  }
                </select>
              </label>
              <label class="field currency">
                <span>Currency</span>
                <input name="currency" maxlength="3" [(ngModel)]="currency" />
              </label>
              <button type="submit" class="button primary" [disabled]="busy()">Open account</button>
            </div>
            @if (error()) {
              <p class="note error" role="alert">{{ error() }}</p>
            }
          </form>
        }

        @if (accounts().state === 'error') {
          <p class="note error" role="alert">{{ accounts().problem?.detail }}</p>
        } @else if (accounts().value; as list) {
          @if (list.length === 0) {
            <p class="note">No accounts have been opened yet.</p>
          } @else {
            <div class="scroll">
              <table class="ledger">
                <thead>
                  <tr>
                    <th scope="col">Code</th>
                    <th scope="col">Account</th>
                    <th scope="col">Type</th>
                    <th scope="col" class="wide-only">Currency</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  @for (account of list; track account.id) {
                    <tr>
                      <td class="code">{{ account.code }}</td>
                      <td>
                        <a [routerLink]="['/accounts', account.id]">{{ account.name }}</a>
                      </td>
                      <td class="muted">{{ account.type }}</td>
                      <td class="code wide-only">{{ account.currency }}</td>
                      <td class="muted">{{ account.isActive ? 'Active' : 'Inactive' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        } @else {
          <p class="muted">Loading</p>
        }
      </div>
    </section>
  `,
  styles: `
    .open {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      padding-bottom: var(--space-5);
      border-bottom: var(--hairline) solid var(--rule);
    }

    .code input {
      width: 7rem;
    }

    .name input {
      width: 18rem;
      max-width: 100%;
    }

    .currency input {
      width: 5rem;
      text-transform: uppercase;
    }
  `,
})
export class AccountsPage {
  private readonly api = inject(Api);

  protected readonly canOpen = inject(Auth).has('Controller');
  protected readonly types: AccountType[] = ['Asset', 'Liability', 'Equity', 'Revenue', 'Expense'];
  protected readonly accounts = loaded<Account[]>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected code = '';
  protected name = '';
  protected type: AccountType = 'Asset';
  protected currency = 'CAD';

  constructor() {
    this.refresh();
  }

  protected open(): void {
    this.busy.set(true);
    this.error.set('');

    this.api
      .openAccount({ code: this.code, name: this.name, type: this.type, currency: this.currency })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.code = '';
          this.name = '';
          this.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.error.set(toProblem(error).detail);
        },
      });
  }

  private refresh(): void {
    load(this.api.accounts(), this.accounts);
  }
}
