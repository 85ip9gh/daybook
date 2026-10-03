import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { Auth, Role, Session } from '../core/auth';
import { Shell } from './shell';

async function linksFor(roles: Role[]): Promise<string[]> {
  const session: Session = { token: 't', name: 'amira', roles, expiresAt: '2099-01-01T00:00:00Z' };
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: Auth, useValue: { user: signal(session), signOut: () => undefined } },
    ],
  });
  const fixture = TestBed.createComponent(Shell);
  await fixture.whenStable();

  return [...(fixture.nativeElement as HTMLElement).querySelectorAll('nav a')].map(
    (link) => link.textContent?.trim() ?? '',
  );
}

describe('Shell', () => {
  it('shows a viewer the books but not the journal entry form', async () => {
    expect(await linksFor(['Viewer'])).toEqual([
      'Trial balance',
      'Accounts',
      'Customers',
      'Invoices',
      'Aged receivables',
      'Reconciliation',
      'Periods',
    ]);
  });

  it('adds the journal entry form for an approver', async () => {
    expect(await linksFor(['Approver'])).toContain('New journal entry');
  });

  it('shows an administrator the users screen and no books', async () => {
    expect(await linksFor(['Admin'])).toEqual(['Users']);
  });
});
