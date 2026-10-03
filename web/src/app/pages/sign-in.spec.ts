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
