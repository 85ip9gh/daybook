import { Component, computed, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth, READERS, Role } from '../core/auth';

interface NavItem {
  label: string;
  path: string;
  roles: Role[];
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

const navigation: NavGroup[] = [
  {
    title: 'Books',
    items: [
      { label: 'Trial balance', path: '/trial-balance', roles: READERS },
      { label: 'Accounts', path: '/accounts', roles: READERS },
      { label: 'New journal entry', path: '/journal/new', roles: ['Approver'] },
    ],
  },
  {
    title: 'Receivables',
    items: [
      { label: 'Customers', path: '/receivables/customers', roles: READERS },
      { label: 'Invoices', path: '/receivables/invoices', roles: READERS },
      { label: 'Aged receivables', path: '/receivables/aged', roles: READERS },
    ],
  },
  {
    title: 'Banking',
    items: [{ label: 'Reconciliation', path: '/banking', roles: READERS }],
  },
  {
    title: 'Control',
    items: [
      { label: 'Periods', path: '/periods', roles: READERS },
      { label: 'Users', path: '/admin/users', roles: ['Admin'] },
    ],
  },
];

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  template: `
    <div class="shell">
      <aside class="rail">
        <a class="brand" routerLink="/">Ledger</a>

        <nav aria-label="Primary">
          @for (group of groups(); track group.title) {
            <div class="group">
              <h3>{{ group.title }}</h3>
              <ul>
                @for (item of group.items; track item.path) {
                  <li>
                    <a [routerLink]="item.path" routerLinkActive="current">{{ item.label }}</a>
                  </li>
                }
              </ul>
            </div>
          }
        </nav>

        <div class="who">
          <span class="name">{{ user()?.name }}</span>
          <span class="roles">{{ user()?.roles?.join(', ') }}</span>
          <button type="button" class="button quiet" (click)="signOut()">Sign out</button>
        </div>
      </aside>

      <main>
        <router-outlet />
      </main>
    </div>
  `,
  styles: `
    .shell {
      display: grid;
      grid-template-columns: var(--rail) minmax(0, 1fr);
      min-height: 100vh;
    }

    .rail {
      position: sticky;
      top: 0;
      display: flex;
      flex-direction: column;
      gap: var(--space-6);
      height: 100vh;
      padding: var(--space-6) var(--space-5);
      border-right: var(--hairline) solid var(--rule);
      background: var(--surface);
    }

    .brand {
      font-size: var(--text-lg);
      font-weight: 600;
      letter-spacing: 0.14em;
      text-transform: uppercase;
      color: var(--ink);
    }

    .brand:hover {
      color: var(--accent);
      text-decoration: none;
    }

    nav {
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
    }

    ul {
      margin: var(--space-2) 0 0;
      padding: 0;
      list-style: none;
    }

    li a {
      display: block;
      margin-left: calc(var(--space-3) * -1);
      padding: var(--space-1) var(--space-3);
      border-left: var(--focus) solid transparent;
      color: var(--ink-soft);
      transition:
        color var(--ease),
        border-color var(--ease);
    }

    li a:hover {
      color: var(--ink);
      text-decoration: none;
    }

    li a.current {
      border-left-color: var(--accent);
      color: var(--ink);
      font-weight: 500;
    }

    .who {
      display: flex;
      flex-direction: column;
      align-items: flex-start;
      gap: var(--space-1);
      margin-top: auto;
      padding-top: var(--space-4);
      border-top: var(--hairline) solid var(--rule);
    }

    .name {
      font-weight: 500;
    }

    .roles {
      font-size: var(--text-sm);
      color: var(--ink-soft);
    }

    .who .button {
      margin: var(--space-2) 0 0 calc(var(--space-2) * -1);
    }

    @media (max-width: 48rem) {
      .shell {
        grid-template-columns: minmax(0, 1fr);
      }

      .rail {
        position: static;
        flex-direction: row;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--space-3) var(--space-5);
        height: auto;
        padding: var(--space-3) var(--space-4);
        border-right: 0;
        border-bottom: var(--hairline) solid var(--rule);
      }

      nav {
        order: 3;
        flex-direction: row;
        flex-basis: 100%;
        gap: var(--space-5);
        overflow-x: auto;
        scrollbar-width: none;
      }

      .group {
        flex: none;
      }

      ul {
        display: flex;
        gap: var(--space-4);
        margin-top: var(--space-1);
      }

      li a {
        margin-left: 0;
        padding: var(--space-1) 0;
        border-left: 0;
        border-bottom: var(--focus) solid transparent;
        white-space: nowrap;
      }

      li a.current {
        border-bottom-color: var(--accent);
      }

      .who {
        flex-direction: row;
        align-items: center;
        gap: var(--space-3);
        margin: 0 0 0 auto;
        padding: 0;
        border-top: 0;
      }

      .roles {
        display: none;
      }

      .who .button {
        margin: 0;
      }
    }
  `,
})
export class Shell {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  protected readonly user = this.auth.user;

  protected readonly groups = computed(() => {
    const roles = this.user()?.roles ?? [];
    return navigation
      .map((group) => ({
        ...group,
        items: group.items.filter((item) => item.roles.some((role) => roles.includes(role))),
      }))
      .filter((group) => group.items.length > 0);
  });

  protected signOut(): void {
    this.auth.signOut();
    void this.router.navigate(['/sign-in']);
  }
}
