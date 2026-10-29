import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRouteSnapshot, NavigationEnd, Router } from '@angular/router';
import { filter, map } from 'rxjs';
import { basics, topics } from './topics';

function deepestPath(route: ActivatedRouteSnapshot): string | undefined {
  let current = route;
  while (current.firstChild) {
    current = current.firstChild;
  }
  return current.routeConfig?.path;
}

@Component({
  selector: 'app-help',
  host: { '(document:keydown.escape)': 'close()' },
  template: `
    @if (topic(); as topic) {
      <button
        #trigger
        type="button"
        class="trigger"
        aria-label="Help: what this page is for and what its words mean"
        [attr.aria-expanded]="open()"
        aria-controls="help-panel"
        (click)="open() ? close() : show()"
      >
        ?
      </button>

      @if (open()) {
        <aside
          #panel
          id="help-panel"
          class="panel"
          role="region"
          aria-labelledby="help-title"
          tabindex="-1"
        >
          <header>
            <div>
              <span class="kicker">Help</span>
              <h2 id="help-title">{{ topic.title }}</h2>
            </div>
            <button type="button" class="button quiet" (click)="close()">Close</button>
          </header>

          <section>
            <h3>What this page is for</h3>
            <p>{{ topic.purpose }}</p>
          </section>

          @if (topic.terms.length > 0) {
            <section>
              <h3>Words on this page</h3>
              <dl>
                @for (item of topic.terms; track item.term) {
                  <dt>{{ item.term }}</dt>
                  <dd>{{ item.meaning }}</dd>
                }
              </dl>
            </section>
          }

          <section>
            <h3>Basic words</h3>
            <dl>
              @for (item of basics; track item.term) {
                <dt>{{ item.term }}</dt>
                <dd>{{ item.meaning }}</dd>
              }
            </dl>
          </section>
        </aside>
      }
    }
  `,
  styles: `
    .trigger {
      position: fixed;
      right: var(--space-5);
      bottom: var(--space-5);
      z-index: 20;
      display: grid;
      place-items: center;
      width: 2.75rem;
      height: 2.75rem;
      background: var(--surface);
      border: var(--hairline) solid var(--rule-firm);
      border-radius: 50%;
      color: var(--accent);
      font-size: var(--text-xl);
      font-weight: 600;
      cursor: pointer;
      transition:
        border-color var(--ease),
        background-color var(--ease);
    }

    .trigger:hover {
      border-color: var(--accent);
      background: var(--accent-wash);
    }

    .trigger[aria-expanded='true'] {
      display: none;
    }

    .panel {
      position: fixed;
      top: 0;
      right: 0;
      bottom: 0;
      z-index: 10;
      display: flex;
      flex-direction: column;
      gap: var(--space-5);
      width: min(28rem, 100%);
      padding: var(--space-6) var(--space-5) var(--space-8);
      overflow-y: auto;
      background: var(--paper);
      border-left: var(--hairline) solid var(--rule-firm);
    }

    .panel:focus-visible {
      outline-offset: calc(var(--focus) * -1);
    }

    header {
      display: flex;
      align-items: flex-start;
      justify-content: space-between;
      gap: var(--space-4);
      padding-bottom: var(--space-4);
      border-bottom: var(--hairline) solid var(--ink);
    }

    .kicker {
      font-size: var(--text-xs);
      font-weight: 500;
      letter-spacing: 0.06em;
      text-transform: uppercase;
      color: var(--ink-soft);
    }

    h2 {
      margin-top: var(--space-1);
      font-size: var(--text-xl);
    }

    p {
      margin-top: var(--space-2);
      line-height: 1.55;
    }

    dl {
      margin: var(--space-3) 0 0;
    }

    dt {
      font-weight: 600;
    }

    dd {
      margin: var(--space-1) 0 var(--space-4);
      color: var(--ink-soft);
      font-size: var(--text-sm);
      line-height: 1.55;
    }

    @media (max-width: 48rem) {
      .trigger {
        right: var(--space-4);
        bottom: var(--space-4);
      }

      .panel {
        padding: var(--space-5) var(--space-4) var(--space-8);
        border-left: 0;
      }
    }
  `,
})
export class Help {
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  private readonly panel = viewChild<ElementRef<HTMLElement>>('panel');

  private readonly path = toSignal(
    this.router.events.pipe(
      filter((event) => event instanceof NavigationEnd),
      map(() => deepestPath(this.router.routerState.snapshot.root)),
    ),
    { initialValue: deepestPath(this.router.routerState.snapshot.root) },
  );

  protected readonly basics = basics;
  protected readonly topic = computed(() => {
    const path = this.path();
    return path === undefined ? undefined : topics[path];
  });
  protected readonly open = signal(false);

  protected show(): void {
    this.open.set(true);
    afterNextRender(() => this.panel()?.nativeElement.focus(), { injector: this.injector });
  }

  protected close(): void {
    if (!this.open()) {
      return;
    }

    this.open.set(false);
    afterNextRender(() => this.trigger()?.nativeElement.focus(), { injector: this.injector });
  }
}
