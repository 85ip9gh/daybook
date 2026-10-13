import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, User, toProblem } from '../core/api';
import { Auth, Role } from '../core/auth';
import { load, loaded } from '../core/loaded';

const allRoles: Role[] = ['Viewer', 'Clerk', 'Approver', 'Controller', 'Admin'];

@Component({
  selector: 'app-users',
  imports: [FormsModule],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Users</h1>
          <p>Role changes apply at next sign-in.</p>
        </div>
      </header>

      <div class="stack">
        <form class="inline-form" (ngSubmit)="create()" novalidate>
          <h3>Add a user</h3>
          <div class="controls">
            <label class="field">
              <span>User name</span>
              <input
                name="userName"
                autocomplete="off"
                autocapitalize="none"
                spellcheck="false"
                [(ngModel)]="userName"
              />
            </label>
            <label class="field">
              <span>Password, 12 characters or more</span>
              <input
                name="password"
                type="password"
                autocomplete="new-password"
                [(ngModel)]="password"
              />
            </label>
          </div>
          <fieldset>
            <legend>Roles</legend>
            @for (role of roles; track role) {
              <label class="check">
                <input
                  type="checkbox"
                  [name]="'new' + role"
                  [checked]="chosen().includes(role)"
                  (change)="toggle(chosen, role)"
                />
                {{ role }}
              </label>
            }
          </fieldset>
          <div>
            <button type="submit" class="button primary" [disabled]="busy()">Add user</button>
          </div>
        </form>

        @if (error()) {
          <p class="note error" role="alert">{{ error() }}</p>
        }

        @if (users().state === 'error') {
          <p class="note error" role="alert">{{ users().problem?.detail }}</p>
        } @else if (users().value; as list) {
          <div class="scroll">
            <table class="ledger plain">
              <thead>
                <tr>
                  <th scope="col">User</th>
                  <th scope="col">Roles</th>
                  <th scope="col">Status</th>
                  <th scope="col"><span class="visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (user of list; track user.id) {
                  <tr>
                    <td>{{ user.userName }}</td>
                    <td>
                      @if (editing() === user.id) {
                        <fieldset>
                          <legend class="visually-hidden">Roles for {{ user.userName }}</legend>
                          @for (role of roles; track role) {
                            <label class="check">
                              <input
                                type="checkbox"
                                [name]="user.id + role"
                                [checked]="edited().includes(role)"
                                (change)="toggle(edited, role)"
                              />
                              {{ role }}
                            </label>
                          }
                        </fieldset>
                      } @else {
                        <span class="muted">{{ user.roles.join(', ') }}</span>
                      }
                    </td>
                    <td>
                      <span class="status" [class]="user.isActive ? 'paid' : 'void'">{{
                        user.isActive ? 'Active' : 'Inactive'
                      }}</span>
                    </td>
                    <td class="row-actions">
                      @if (editing() === user.id) {
                        <button type="button" class="button primary" (click)="save(user)">
                          Save roles
                        </button>
                        <button type="button" class="button quiet" (click)="editing.set('')">
                          Cancel
                        </button>
                      } @else if (confirming() === user.id) {
                        <button type="button" class="button primary" (click)="deactivate(user)">
                          Confirm, deactivate
                        </button>
                        <button type="button" class="button quiet" (click)="confirming.set('')">
                          Cancel
                        </button>
                      } @else {
                        <button type="button" class="button quiet" (click)="edit(user)">
                          Change roles
                        </button>
                        @if (user.isActive && user.userName !== me) {
                          <button
                            type="button"
                            class="button quiet"
                            (click)="confirming.set(user.id)"
                          >
                            Deactivate
                          </button>
                        }
                      }
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        } @else {
          <p class="muted">Loading</p>
        }
      </div>
    </section>
  `,
  styles: `
    .row-actions {
      text-align: right;
      white-space: nowrap;
    }

    .row-actions .button + .button {
      margin-left: var(--space-2);
    }
  `,
})
export class UsersPage {
  private readonly api = inject(Api);

  protected readonly me = inject(Auth).user()?.name ?? '';
  protected readonly roles = allRoles;
  protected readonly users = loaded<User[]>();
  protected readonly chosen = signal<Role[]>(['Viewer']);
  protected readonly edited = signal<Role[]>([]);
  protected readonly editing = signal('');
  protected readonly confirming = signal('');
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected userName = '';
  protected password = '';

  constructor() {
    this.refresh();
  }

  protected toggle(target: typeof this.chosen, role: Role): void {
    target.update((held) =>
      held.includes(role) ? held.filter((other) => other !== role) : [...held, role],
    );
  }

  protected edit(user: User): void {
    this.edited.set([...user.roles]);
    this.editing.set(user.id);
    this.confirming.set('');
  }

  protected create(): void {
    this.run(this.api.createUser(this.userName, this.password, this.ordered(this.chosen())), () => {
      this.userName = '';
      this.password = '';
      this.chosen.set(['Viewer']);
    });
  }

  protected save(user: User): void {
    this.run(this.api.assignRoles(user.id, this.ordered(this.edited())), () =>
      this.editing.set(''),
    );
  }

  protected deactivate(user: User): void {
    this.run(this.api.deactivateUser(user.id), () => this.confirming.set(''));
  }

  private ordered(roles: Role[]): Role[] {
    return allRoles.filter((role) => roles.includes(role));
  }

  private run(action: ReturnType<Api['deactivateUser']>, done: () => void): void {
    this.busy.set(true);
    this.error.set('');

    action.subscribe({
      next: () => {
        this.busy.set(false);
        done();
        this.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.password = '';
        this.error.set(toProblem(error).detail);
      },
    });
  }

  private refresh(): void {
    load(this.api.users(), this.users);
  }
}
