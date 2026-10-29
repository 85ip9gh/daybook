import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { Route, Router, provideRouter } from '@angular/router';
import { routes } from '../app.routes';
import { Help } from './help';
import { topics } from './topics';

@Component({ template: '' })
class Blank {}

function routedPaths(list: Route[], prefix = ''): string[] {
  return list.flatMap((route) => {
    const path = [prefix, route.path].filter(Boolean).join('/');
    const own = route.loadComponent && route.path ? [route.path] : [];
    return [...own, ...routedPaths(route.children ?? [], path)];
  });
}

describe('Help', () => {
  async function open(url: string) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([
          { path: 'periods', component: Blank },
          { path: 'receivables/invoices/:id', component: Blank },
          { path: 'unknown', component: Blank },
        ]),
      ],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(Help);
    await fixture.whenStable();
    return { page: fixture.nativeElement as HTMLElement, fixture };
  }

  it('has a help topic for every page in the app', () => {
    const missing = routedPaths(routes).filter((path) => !(path in topics));

    expect(missing).toEqual([]);
  });

  it('explains the page that is open, then the basic words', async () => {
    const { page, fixture } = await open('/receivables/invoices/42');

    page.querySelector<HTMLButtonElement>('.trigger')?.click();
    await fixture.whenStable();

    expect(page.querySelector('#help-title')?.textContent?.trim()).toBe('Invoice');
    const terms = [...page.querySelectorAll('dt')].map((term) => term.textContent?.trim());
    expect(terms).toContain('Void this invoice');
    expect(terms).toContain('Debit and credit');
  });

  it('closes on Escape and gives focus back to the button', async () => {
    const { page, fixture } = await open('/periods');
    const trigger = page.querySelector<HTMLButtonElement>('.trigger');

    trigger?.click();
    await fixture.whenStable();
    expect(page.querySelector('.panel')).not.toBeNull();

    document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' }));
    await fixture.whenStable();

    expect(page.querySelector('.panel')).toBeNull();
    expect(document.activeElement).toBe(page.querySelector('.trigger'));
  });

  it('shows no button on a page without a help topic', async () => {
    const { page } = await open('/unknown');

    expect(page.querySelector('.trigger')).toBeNull();
  });
});
