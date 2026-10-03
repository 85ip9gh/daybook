import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api, Customer, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { load, loaded } from '../core/loaded';

@Component({
  selector: 'app-customers',
  imports: [FormsModule, RouterLink],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Customers</h1>
          <p>Open a customer to see what they owe and to record a payment.</p>
        </div>
      </header>

      <div class="stack">
        @if (canRegister) {
          <form class="inline-form" (ngSubmit)="register()" novalidate>
            <h3>Register a customer</h3>
            <div class="controls">
              <label class="field narrow">
                <span>Code</span>
                <input name="code" maxlength="20" [(ngModel)]="code" />
              </label>
              <label class="field wide">
                <span>Name</span>
                <input name="name" maxlength="100" [(ngModel)]="name" />
              </label>
              <button type="submit" class="button primary" [disabled]="busy()">
                Register customer
              </button>
            </div>
            @if (error()) {
              <p class="note error" role="alert">{{ error() }}</p>
            }
          </form>
        }

        @if (customers().state === 'error') {
          <p class="note error" role="alert">{{ customers().problem?.detail }}</p>
        } @else if (customers().value; as list) {
          @if (list.length === 0) {
            <p class="note">No customers have been registered yet.</p>
          } @else {
            <div class="scroll">
              <table class="ledger">
                <thead>
                  <tr>
                    <th scope="col">Code</th>
                    <th scope="col">Customer</th>
                  </tr>
                </thead>
                <tbody>
                  @for (customer of list; track customer.id) {
                    <tr>
                      <td class="code">{{ customer.code }}</td>
                      <td>
                        <a [routerLink]="['/receivables/customers', customer.id]">{{
                          customer.name
                        }}</a>
                      </td>
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
})
export class CustomersPage {
  private readonly api = inject(Api);

  protected readonly canRegister = inject(Auth).has('Clerk');
  protected readonly customers = loaded<Customer[]>();
  protected readonly busy = signal(false);
  protected readonly error = signal('');

  protected code = '';
  protected name = '';

  constructor() {
    this.refresh();
  }

  protected register(): void {
    this.busy.set(true);
    this.error.set('');

    this.api.registerCustomer({ code: this.code, name: this.name }).subscribe({
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
    load(this.api.customers(), this.customers);
  }
}
