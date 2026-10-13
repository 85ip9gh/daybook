import { DatePipe } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api, Periods, toProblem } from '../core/api';
import { Auth } from '../core/auth';
import { monthLabel, previousMonth, today } from '../core/dates';
import { load, loaded } from '../core/loaded';

@Component({
  selector: 'app-periods',
  imports: [FormsModule, DatePipe],
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Periods</h1>
          <p>
            @if (periods().value?.closedThrough; as closed) {
              Closed through <span class="date">{{ closed }}</span
              >.
            } @else {
              No periods closed.
            }
          </p>
        </div>
      </header>

      <div class="stack">
        @if (canClose) {
          <form class="close" (ngSubmit)="confirming.set(true)" novalidate>
            <h3>Close a period</h3>
            <div class="controls">
              <label class="field">
                <span>Month</span>
                <input
                  type="month"
                  name="month"
                  [ngModel]="month()"
                  (ngModelChange)="pick($event)"
                />
              </label>
              @if (!confirming()) {
                <button type="submit" class="button" [disabled]="!month() || busy()">
                  Close {{ label() }}
                </button>
              } @else {
                <button type="button" class="button primary" [disabled]="busy()" (click)="close()">
                  Confirm, close {{ label() }}
                </button>
                <button type="button" class="button quiet" (click)="confirming.set(false)">
                  Cancel
                </button>
              }
            </div>
            @if (confirming()) {
              <p class="note">This cannot be undone. Earlier months close with it.</p>
            }
            @if (error()) {
              <p class="note error" role="alert">{{ error() }}</p>
            }
          </form>
        }

        @if (periods().state === 'error') {
          <p class="note error" role="alert">{{ periods().problem?.detail }}</p>
        } @else if (periods().value; as found) {
          @if (found.closes.length > 0) {
            <div class="scroll">
              <table class="ledger">
                <thead>
                  <tr>
                    <th scope="col">Closed through</th>
                    <th scope="col">Closed on</th>
                    <th scope="col">Closed by</th>
                  </tr>
                </thead>
                <tbody>
                  @for (close of found.closes; track close.periodEnd) {
                    <tr>
                      <td class="code">{{ close.periodEnd }}</td>
                      <td class="code">{{ close.closedAt | date: 'yyyy-MM-dd HH:mm' }}</td>
                      <td>{{ close.closedBy }}</td>
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
    .close {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      padding-bottom: var(--space-5);
      border-bottom: var(--hairline) solid var(--rule);
    }

    .date {
      font-family: var(--font-figure);
      font-size: var(--text-sm);
      color: var(--ink);
    }
  `,
})
export class PeriodsPage {
  private readonly api = inject(Api);

  protected readonly canClose = inject(Auth).has('Controller');
  protected readonly periods = loaded<Periods>();
  protected readonly month = signal(previousMonth(today()));
  protected readonly confirming = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal('');
  protected readonly label = computed(() => (this.month() ? monthLabel(this.month()) : 'a month'));

  constructor() {
    this.refresh();
  }

  protected pick(month: string): void {
    this.month.set(month);
    this.confirming.set(false);
    this.error.set('');
  }

  protected close(): void {
    const [year, month] = this.month().split('-').map(Number);
    this.busy.set(true);
    this.error.set('');

    this.api.closePeriod(year, month).subscribe({
      next: () => {
        this.busy.set(false);
        this.confirming.set(false);
        this.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.confirming.set(false);
        this.error.set(toProblem(error).detail);
      },
    });
  }

  private refresh(): void {
    load(this.api.periods(), this.periods);
  }
}
