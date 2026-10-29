import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { toProblem } from '../core/api';
import { Auth, Role } from '../core/auth';
import { AppConfig } from '../core/config';

const demoRoles: Role[] = ['Viewer', 'Clerk', 'Approver', 'Controller'];

const tasks: { task: string; roles: Role[] }[] = [
  { task: 'See the books and every report', roles: demoRoles },
  { task: 'Add a new customer', roles: ['Clerk'] },
  { task: 'Write an invoice as a draft', roles: ['Clerk'] },
  { task: 'Record a payment a customer sent', roles: ['Clerk'] },
  { task: 'Upload a bank statement', roles: ['Clerk'] },
  { task: 'Make a draft invoice final, or cancel an unpaid one', roles: ['Approver'] },
  { task: 'Make an entry in the books, or reverse one', roles: ['Approver'] },
  { task: 'Pair a bank line with an entry in the books by hand', roles: ['Approver'] },
  { task: 'Add a new account to the books, such as Rent or Sales', roles: ['Controller'] },
  { task: 'Close a month so nothing in it can change', roles: ['Controller'] },
];

@Component({
  selector: 'app-sign-in',
  imports: [FormsModule],
  template: `
    <div class="frame">
      <aside class="band">
        <span class="brand">Daybook</span>
        <p>Accounting for a small firm: its books, its invoices, and a check against the bank.</p>
      </aside>

      <main>
        <form (ngSubmit)="submit()" novalidate>
          <h1>Sign in</h1>

          <label class="field">
            <span>User name</span>
            <input
              name="userName"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
              required
              [(ngModel)]="userName"
            />
          </label>

          <label class="field">
            <span>Password</span>
            <input
              name="password"
              type="password"
              autocomplete="current-password"
              required
              [(ngModel)]="password"
            />
          </label>

          @if (error()) {
            <p class="note error" role="alert">{{ error() }}</p>
          }

          <button type="submit" class="button primary" [disabled]="busy()">
            {{ busy() ? 'Signing in' : 'Sign in' }}
          </button>
        </form>

        @if (demo; as demo) {
          <section class="demo" aria-labelledby="demo-heading">
            <h2 id="demo-heading">Try the demo</h2>
            <p>
              Made-up books for a small consulting firm. Anyone can sign in and try it, and it
              resets every night. Each login is a different job, so each one sees and can do
              different things. The person who writes an invoice can never be the one who approves
              it.
            </p>
            <ul>
              @for (account of demo.accounts; track account.userName) {
                <li>
                  <button
                    type="button"
                    class="button"
                    [disabled]="busy()"
                    (click)="signInAs(account.userName, demo.password)"
                  >
                    {{ account.role }}
                  </button>
                  <span>{{ account.summary }}</span>
                </li>
              }
            </ul>

            <h3 id="roles-heading">Who can do what</h3>
            <div class="scroll">
              <table class="ledger roles" aria-labelledby="roles-heading">
                <thead>
                  <tr>
                    <th scope="col">Task</th>
                    @for (role of demoRoles; track role) {
                      <th scope="col" class="mark">{{ role }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of tasks; track row.task) {
                    <tr>
                      <td>{{ row.task }}</td>
                      @for (role of demoRoles; track role) {
                        <td class="mark">
                          @if (row.roles.includes(role)) {
                            <span aria-hidden="true">✓</span
                            ><span class="visually-hidden">Yes</span>
                          } @else {
                            <span class="visually-hidden">No</span>
                          }
                        </td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <p>
              A fifth job, Admin, decides who gets a login and which job they have, and cannot see
              any numbers. It has no demo login.
            </p>
          </section>
        }
      </main>
    </div>
  `,
  styles: `
    .frame {
      display: grid;
      grid-template-columns: var(--rail) minmax(0, 1fr);
      min-height: 100vh;
    }

    .band {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      padding: var(--space-6) var(--space-5);
      background: var(--accent);
      color: var(--paper);
    }

    .brand {
      font-size: var(--text-lg);
      font-weight: 600;
      letter-spacing: 0.14em;
      text-transform: uppercase;
    }

    .band p {
      font-size: var(--text-sm);
      line-height: 1.5;
      opacity: 0.86;
    }

    main {
      padding: var(--space-8) var(--space-6);
    }

    form {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
      max-width: 20rem;
    }

    h1 {
      margin-bottom: var(--space-2);
      font-size: var(--text-2xl);
    }

    form .button {
      align-self: flex-start;
      margin-top: var(--space-2);
    }

    .demo {
      max-width: var(--measure);
      margin-top: var(--space-8);
      padding-top: var(--space-5);
      border-top: var(--hairline) solid var(--rule);
    }

    .demo h2 {
      font-size: var(--text-lg);
    }

    .demo p {
      margin-top: var(--space-2);
      color: var(--ink-soft);
      font-size: var(--text-sm);
      line-height: 1.5;
    }

    .demo ul {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-top: var(--space-4);
      padding: 0;
      list-style: none;
    }

    .demo li {
      display: grid;
      grid-template-columns: 8rem minmax(0, 1fr);
      gap: var(--space-4);
      align-items: center;
    }

    .demo li span {
      color: var(--ink-soft);
      font-size: var(--text-sm);
    }

    .demo h3 {
      margin-top: var(--space-6);
      font-size: var(--text-md);
    }

    .demo .scroll {
      margin-top: var(--space-3);
    }

    .roles {
      font-size: var(--text-sm);
    }

    .roles .mark {
      width: 6.5rem;
      text-align: center;
    }

    .roles td.mark {
      color: var(--accent);
      font-weight: 600;
    }

    @media (max-width: 48rem) {
      .frame {
        grid-template-columns: minmax(0, 1fr);
        grid-template-rows: auto 1fr;
      }

      .band {
        padding: var(--space-4);
      }

      main {
        padding: var(--space-6) var(--space-4);
      }

      .roles .mark {
        width: auto;
        padding-inline: var(--space-2);
      }

      .roles th {
        letter-spacing: 0;
        text-transform: none;
      }
    }
  `,
})
export class SignIn {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  protected readonly demo = inject(AppConfig).demo;
  protected readonly demoRoles = demoRoles;
  protected readonly tasks = tasks;
  protected userName = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (!this.userName.trim() || !this.password) {
      this.error.set('Enter your user name and password.');
      return;
    }

    this.signInWith(this.userName.trim(), this.password);
  }

  protected signInAs(userName: string, password: string): void {
    this.userName = userName;
    this.password = '';
    this.signInWith(userName, password);
  }

  private signInWith(userName: string, password: string): void {
    this.busy.set(true);
    this.error.set('');

    this.auth.signIn(userName, password).subscribe({
      next: () => void this.router.navigate(['/']),
      error: (error: unknown) => {
        this.busy.set(false);
        this.password = '';
        this.error.set(toProblem(error).detail);
      },
    });
  }
}
