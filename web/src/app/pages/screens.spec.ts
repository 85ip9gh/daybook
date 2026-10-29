import { HttpErrorResponse } from '@angular/common/http';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { Account, Api, Invoice, Reconciliation, User } from '../core/api';
import { Auth, Role } from '../core/auth';
import { AccountsPage } from './accounts';
import { CustomerPage } from './customer';
import { ReconciliationPage } from './reconciliation';
import { UsersPage } from './users';

const bank: Account = {
  id: 'bank',
  code: '1000',
  name: 'Operating account',
  type: 'Asset',
  currency: 'CAD',
  isActive: true,
  isBank: true,
};

const receivable: Account = {
  id: 'receivable',
  code: '1200',
  name: 'Accounts receivable',
  type: 'Asset',
  currency: 'CAD',
  isActive: true,
  isBank: false,
};

function auth(name: string, ...roles: Role[]) {
  return {
    provide: Auth,
    useValue: {
      user: signal({ name, roles, token: 't', expiresAt: '2099-01-01T00:00:00Z' }),
      has: (...wanted: Role[]) => wanted.some((role) => roles.includes(role)),
    },
  };
}

async function settle<T>(fixture: ComponentFixture<T>): Promise<HTMLElement> {
  await fixture.whenStable();
  return fixture.nativeElement as HTMLElement;
}

async function type(fixture: ComponentFixture<unknown>, control: Element, value: string) {
  (control as HTMLInputElement).value = value;
  control.dispatchEvent(new Event(control instanceof HTMLSelectElement ? 'change' : 'input'));
  await fixture.whenStable();
}

describe('UsersPage', () => {
  const users: User[] = [
    { id: 'u1', userName: 'amira', roles: ['Admin'], isActive: true },
    { id: 'u2', userName: 'jonas', roles: ['Viewer'], isActive: true },
  ];

  async function render(api: Partial<Api>) {
    TestBed.configureTestingModule({
      providers: [
        { provide: Api, useValue: { users: () => of(users), ...api } },
        auth('amira', 'Admin'),
      ],
    });
    const fixture = TestBed.createComponent(UsersPage);
    return { fixture, page: await settle(fixture) };
  }

  it('does not offer to deactivate the signed-in administrator', async () => {
    const { page } = await render({});
    const rows = [...page.querySelectorAll('tbody tr')].map((row) => row.textContent ?? '');

    expect(rows[0]).toContain('amira');
    expect(rows[0]).not.toContain('Deactivate');
    expect(rows[1]).toContain('Deactivate');
  });

  it('sends the chosen roles in a fixed order however they were ticked', async () => {
    const createUser = vi.fn().mockReturnValue(of(users[1]));
    const { fixture, page } = await render({ createUser });
    const boxes = page.querySelectorAll<HTMLInputElement>('form input[type=checkbox]');

    await type(fixture, page.querySelector('input[name=userName]')!, 'priya');
    await type(fixture, page.querySelector('input[name=password]')!, 'a-long-password');
    boxes[2].dispatchEvent(new Event('change'));
    boxes[1].dispatchEvent(new Event('change'));
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(createUser).toHaveBeenCalledWith('priya', 'a-long-password', [
      'Viewer',
      'Clerk',
      'Approver',
    ]);
  });

  it('clears the typed password when the server refuses the user', async () => {
    const createUser = vi.fn().mockReturnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 422,
            error: { detail: 'A password is 12 to 256 characters.' },
          }),
      ),
    );
    const { fixture, page } = await render({ createUser });

    await type(fixture, page.querySelector('input[name=password]')!, 'short');
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();

    expect(page.querySelector('.note.error')?.textContent).toContain('12 to 256 characters');
    expect(page.querySelector<HTMLInputElement>('input[name=password]')!.value).toBe('');
  });
});

describe('ReconciliationPage', () => {
  const report: Reconciliation = {
    accountId: 'bank',
    asOf: '2026-10-03',
    bookBalance: 100,
    bankTotal: 60,
    unmatchedBankLines: [
      { id: 'b1', date: '2026-10-01', reference: 'BK-1', description: 'Fee', amount: -12.5 },
      { id: 'b2', date: '2026-10-01', reference: 'BK-2', description: 'Interest', amount: 4.18 },
    ],
    unmatchedBankTotal: -8.32,
    unreconciledLedgerLines: [
      { journalEntryId: 'e1', lineNumber: 2, entryDate: '2026-09-25', amount: -12.5 },
    ],
    unreconciledLedgerTotal: -12.5,
    matchedBankLines: [
      {
        id: 'b9',
        date: '2026-09-30',
        reference: 'BK-9',
        description: 'Rent',
        amount: -2300,
        entryDate: '2026-09-29',
        matchedBy: 'auto-match',
      },
    ],
  };

  async function render(
    roles: Role[],
    matchBankLine = vi.fn().mockReturnValue(of(undefined)),
    unmatchBankLine = vi.fn().mockReturnValue(of(undefined)),
  ) {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Api,
          useValue: {
            accounts: () => of([bank, receivable]),
            reconciliation: () => of(report),
            statements: () => of([]),
            matchBankLine,
            unmatchBankLine,
          },
        },
        auth('amira', ...roles),
      ],
    });
    const fixture = TestBed.createComponent(ReconciliationPage);
    const page = await settle(fixture);
    await type(fixture, page.querySelector('select')!, 'bank');
    return { fixture, page, matchBankLine, unmatchBankLine };
  }

  const undoButton = (page: HTMLElement) =>
    [...page.querySelectorAll('button')].find((button) =>
      button.textContent?.includes('Undo match'),
    );

  it('offers only bank accounts to reconcile', async () => {
    const { page } = await render(['Viewer']);
    const options = [...page.querySelectorAll('select option')].map((option) => option.textContent);

    expect(options).toContain('1000 Operating account');
    expect(options).not.toContain('1200 Accounts receivable');
  });

  it('lets an approver undo a match', async () => {
    const { fixture, page, unmatchBankLine } = await render(['Approver']);

    expect(page.textContent).toContain('BK-9');
    expect(page.textContent).toContain('Automatic');

    undoButton(page)!.click();
    await fixture.whenStable();
    expect(unmatchBankLine).toHaveBeenCalledWith('b9');
  });

  const matchButton = (page: HTMLElement) =>
    [...page.querySelectorAll('button')].find((button) => button.textContent?.includes('Match'));

  it('lets an approver match two lines only when their amounts are equal', async () => {
    const { fixture, page, matchBankLine } = await render(['Approver']);
    const radios = page.querySelectorAll<HTMLInputElement>('input[type=radio]');

    radios[1].dispatchEvent(new Event('change'));
    radios[2].dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(matchButton(page)!.disabled).toBe(true);
    expect(page.textContent).toContain('The two amounts are not equal.');

    radios[0].dispatchEvent(new Event('change'));
    await fixture.whenStable();
    expect(matchButton(page)!.disabled).toBe(false);

    matchButton(page)!.click();
    await fixture.whenStable();
    expect(matchBankLine).toHaveBeenCalledWith('b1', 'e1', 2);
  });

  it('shows a viewer the two lists with nothing to choose or import', async () => {
    const { page } = await render(['Viewer']);

    expect(page.querySelectorAll('input[type=radio]')).toHaveLength(0);
    expect(page.querySelector('input[type=file]')).toBeNull();
    expect(matchButton(page)).toBeUndefined();
    expect(undoButton(page)).toBeUndefined();
    expect(page.textContent).toContain('BK-2');
    expect(page.textContent).toContain('BK-9');
  });
});

describe('AccountsPage', () => {
  it('opens a bank account only as an asset', async () => {
    const openAccount = vi.fn().mockReturnValue(of(bank));
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { accounts: () => of([bank, receivable]), openAccount } },
        auth('amira', 'Controller'),
      ],
    });
    const fixture = TestBed.createComponent(AccountsPage);
    const page = await settle(fixture);

    expect(page.textContent).toContain('Asset, bank');
    await type(fixture, page.querySelector('input[name=code]')!, '1020');
    await type(fixture, page.querySelector('input[name=name]')!, 'Payroll account');
    page.querySelector<HTMLInputElement>('input[name=isBank]')!.click();
    await fixture.whenStable();
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
    expect(openAccount).toHaveBeenCalledWith(
      expect.objectContaining({ code: '1020', type: 'Asset', isBank: true }),
    );

    await type(fixture, page.querySelector('select[name=type]')!, 'Revenue');
    expect(page.querySelector('input[name=isBank]')).toBeNull();
  });
});

describe('CustomerPage', () => {
  const invoice = {
    id: 'i1',
    customerId: 'c1',
    number: 3,
    currency: 'CAD',
    issueDate: '2026-08-05',
    dueDate: '2026-09-04',
    total: 5472,
    outstanding: 5472,
  } as Invoice;

  it('retries a failed payment with the same key and sends only what was allocated', async () => {
    const receivePayment = vi
      .fn()
      .mockReturnValueOnce(throwError(() => new HttpErrorResponse({ status: 0 })))
      .mockReturnValue(
        of({
          id: 'p1',
          amount: 1000,
          paymentDate: '2026-10-03',
          receivedBy: 'amira',
          allocations: [],
        }),
      );
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: Api,
          useValue: {
            customers: () => of([{ id: 'c1', code: 'BNA', name: 'Bluenose Analytics' }]),
            accounts: () => of([bank, receivable]),
            receivables: () => of({ customerId: 'c1', outstanding: 5472, invoices: [invoice] }),
            receivePayment,
          },
        },
        auth('amira', 'Clerk'),
      ],
    });
    const fixture = TestBed.createComponent(CustomerPage);
    fixture.componentRef.setInput('id', 'c1');
    const page = await settle(fixture);
    const submit = async () => {
      page.querySelector('form')!.dispatchEvent(new Event('submit'));
      await fixture.whenStable();
    };

    expect(page.querySelector('select[name=deposit]')!.textContent).not.toContain('1200');
    await type(fixture, page.querySelector('input.figure')!, '6000');
    expect(page.querySelector<HTMLButtonElement>('button[type=submit]')!.disabled).toBe(true);
    expect(page.textContent).toContain('more than its invoice has outstanding');

    await type(fixture, page.querySelector('input.figure')!, '1000');
    await type(fixture, page.querySelector('select')!, 'bank');
    await submit();
    await submit();

    const [first, second] = receivePayment.mock.calls;
    expect(first[1]).toBe(second[1]);
    expect(second[0]).toMatchObject({
      customerId: 'c1',
      amount: 1000,
      depositAccountId: 'bank',
      allocations: [{ invoiceId: 'i1', amount: 1000 }],
    });
    expect(page.querySelector('.note.done')?.textContent).toContain('1,000.00');
  });
});
