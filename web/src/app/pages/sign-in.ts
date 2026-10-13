import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { AppConfig } from '../core/config';

@Component({
  selector: 'app-sign-in',
  imports: [FormsModule],
  template: `
    <div class="frame">
      <aside class="band">
        <span class="brand">Daybook</span>
        <p>General ledger, receivables and bank reconciliation.</p>
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
            <p>Sample books, reset nightly. Each role sees different screens.</p>
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
    }
  `,
})
export class SignIn {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  protected readonly demo = inject(AppConfig).demo;
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
