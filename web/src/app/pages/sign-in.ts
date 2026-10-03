import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { toProblem } from '../core/api';
import { Auth } from '../core/auth';

@Component({
  selector: 'app-sign-in',
  imports: [FormsModule],
  template: `
    <div class="frame">
      <aside class="band">
        <span class="brand">Daybook</span>
        <p>Double-entry books, receivables and bank reconciliation.</p>
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

    .button {
      align-self: flex-start;
      margin-top: var(--space-2);
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

  protected userName = '';
  protected password = '';
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected submit(): void {
    if (!this.userName.trim() || !this.password) {
      this.error.set('Enter your user name and password.');
      return;
    }

    this.busy.set(true);
    this.error.set('');

    this.auth.signIn(this.userName.trim(), this.password).subscribe({
      next: () => void this.router.navigate(['/']),
      error: (error: unknown) => {
        this.busy.set(false);
        this.password = '';
        this.error.set(toProblem(error).detail);
      },
    });
  }
}
