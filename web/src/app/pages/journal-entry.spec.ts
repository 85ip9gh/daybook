import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { Account, Api, Entry } from '../core/api';
import { JournalEntryPage } from './journal-entry';

const accounts: Account[] = [
  {
    id: 'bank',
    code: '1000',
    name: 'Operating account',
    type: 'Asset',
    currency: 'CAD',
    isActive: true,
    isBank: true,
  },
  {
    id: 'fees',
    code: '5300',
    name: 'Bank fees',
    type: 'Expense',
    currency: 'CAD',
    isActive: true,
    isBank: false,
  },
  {
    id: 'old',
    code: '1001',
    name: 'Closed account',
    type: 'Asset',
    currency: 'CAD',
    isActive: false,
    isBank: true,
  },
];

const posted: Entry = {
  id: 'entry-1',
  entryDate: '2026-10-03',
  description: 'Wire transfer fee',
  currency: 'CAD',
  postedBy: 'amira',
  lines: [
    { lineNumber: 1, accountId: 'fees', side: 'Debit', amount: 12.5 },
    { lineNumber: 2, accountId: 'bank', side: 'Credit', amount: 12.5 },
  ],
};

describe('JournalEntryPage', () => {
  let fixture: ComponentFixture<JournalEntryPage>;
  let page: HTMLElement;
  let postEntry: ReturnType<typeof vi.fn>;

  beforeEach(async () => {
    postEntry = vi.fn().mockReturnValue(of(posted));
    TestBed.configureTestingModule({
      providers: [{ provide: Api, useValue: { accounts: () => of(accounts), postEntry } }],
    });
    fixture = TestBed.createComponent(JournalEntryPage);
    page = fixture.nativeElement as HTMLElement;
    await fixture.whenStable();
  });

  async function type(selector: string, index: number, value: string): Promise<void> {
    const control = page.querySelectorAll<HTMLInputElement | HTMLSelectElement>(selector)[index];
    control.value = value;
    control.dispatchEvent(new Event(control instanceof HTMLSelectElement ? 'change' : 'input'));
    await fixture.whenStable();
  }

  async function fill(credit: string): Promise<void> {
    await type('.description input', 0, 'Wire transfer fee');
    await type('select', 0, 'fees');
    await type('input.figure', 0, '12.50');
    await type('select', 1, 'bank');
    await type('input.figure', 3, credit);
  }

  async function submit(): Promise<void> {
    page.querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  const button = () => page.querySelector<HTMLButtonElement>('button[type=submit]')!;

  it('offers only active accounts', () => {
    const options = [...page.querySelectorAll('select')[0].options].map((option) => option.value);

    expect(options).toEqual(['', 'bank', 'fees']);
  });

  it('keeps the post button off and says why until the entry balances', async () => {
    await fill('12.00');

    expect(button().disabled).toBe(true);
    expect(page.textContent).toContain('Out of balance by 0.50.');

    await type('input.figure', 3, '12.50');

    expect(button().disabled).toBe(false);
    expect(page.textContent).not.toContain('Out of balance');
  });

  it('posts the lines with an idempotency key and clears the form', async () => {
    await fill('12.50');
    await submit();

    const [entry, key] = postEntry.mock.calls[0];
    expect(entry.lines).toEqual([
      { accountId: 'fees', side: 'Debit', amount: 12.5 },
      { accountId: 'bank', side: 'Credit', amount: 12.5 },
    ]);
    expect(entry.currency).toBe('CAD');
    expect(key).toMatch(/^[0-9a-f-]{36}$/);
    expect(page.querySelector('.note.done')?.textContent).toContain('Posted by amira');
    expect(page.querySelectorAll<HTMLInputElement>('input.figure')[0].value).toBe('');
  });

  it('retries a failed post with the same key and uses a new one after a success', async () => {
    postEntry.mockReturnValueOnce(
      throwError(() => new HttpErrorResponse({ status: 0, statusText: 'Unknown Error' })),
    );
    await fill('12.50');

    await submit();
    expect(page.querySelector('.note.error')?.textContent).toContain('could not be reached');
    await submit();
    await fill('12.50');
    await submit();

    const keys = postEntry.mock.calls.map((call) => call[1]);
    expect(keys[0]).toBe(keys[1]);
    expect(keys[2]).not.toBe(keys[1]);
  });

  it('shows the reason the server gives when it refuses the entry', async () => {
    postEntry.mockReturnValueOnce(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 422,
            error: { code: 'period_closed', detail: 'The books are closed through 2026-08-31.' },
          }),
      ),
    );
    await fill('12.50');
    await submit();

    expect(page.querySelector('.note.error')?.textContent).toContain('closed through 2026-08-31');
  });
});
