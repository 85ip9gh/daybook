import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { Auth } from '../core/auth';
import { AppConfig } from '../core/config';
import { SignIn } from './sign-in';

describe('SignIn', () => {
  async function render(config: object) {
    const signIn = vi.fn((_userName: string, _password: string) => of(undefined));
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: Auth, useValue: { signIn } }],
    });
    TestBed.inject(AppConfig).apply(config);
    const fixture = TestBed.createComponent(SignIn);
    await fixture.whenStable();
    return { page: fixture.nativeElement as HTMLElement, signIn };
  }

  const demo = {
    password: 'daybook-demo',
    accounts: [
      { userName: 'demo-clerk', role: 'Clerk', summary: 'Drafts invoices' },
      { userName: 'demo-approver', role: 'Approver', summary: 'Issues invoices' },
    ],
  };

  function markedRoles(page: HTMLElement, task: string): string[] {
    const roles = [...page.querySelectorAll('.roles thead th')].map((cell) =>
      cell.textContent?.trim(),
    );
    const row = [...page.querySelectorAll<HTMLTableRowElement>('.roles tbody tr')].find(
      (candidate) => candidate.cells[0].textContent?.trim() === task,
    );
    return [...(row?.cells ?? [])]
      .filter((cell) => cell.textContent?.includes('Yes'))
      .map((cell) => roles[cell.cellIndex] ?? '');
  }

  it('shows which demo role can do each task', async () => {
    const { page } = await render({ demo });

    expect(markedRoles(page, 'See the books and every report')).toEqual([
      'Viewer',
      'Clerk',
      'Approver',
      'Controller',
    ]);
    expect(markedRoles(page, 'Write an invoice as a draft')).toEqual(['Clerk']);
    expect(markedRoles(page, 'Make a draft invoice final, or cancel an unpaid one')).toEqual([
      'Approver',
    ]);
    expect(markedRoles(page, 'Close a month so nothing in it can change')).toEqual(['Controller']);
  });

  it('shows no demo section unless the config names demo accounts', async () => {
    const { page } = await render({});

    expect(page.querySelector('.demo')).toBeNull();
  });

  it('ignores a demo section without a password', async () => {
    const { page } = await render({ demo: { accounts: demo.accounts } });

    expect(page.querySelector('.demo')).toBeNull();
  });

  it('signs in as the demo account whose button was pressed', async () => {
    const { page, signIn } = await render({ demo });
    const buttons = [...page.querySelectorAll<HTMLButtonElement>('.demo button')];

    expect(buttons.map((button) => button.textContent?.trim())).toEqual(['Clerk', 'Approver']);

    buttons[1].click();

    expect(signIn).toHaveBeenCalledExactlyOnceWith('demo-approver', 'daybook-demo');
  });
});
