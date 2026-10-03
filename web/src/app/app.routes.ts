import { Routes } from '@angular/router';
import { READERS, holding, signedIn } from './core/auth';

export const routes: Routes = [
  {
    path: 'sign-in',
    title: 'Sign in · Ledger',
    loadComponent: () => import('./pages/sign-in').then((module) => module.SignIn),
  },
  {
    path: '',
    canActivate: [signedIn],
    loadComponent: () => import('./shell/shell').then((module) => module.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'trial-balance' },
      {
        path: 'trial-balance',
        title: 'Trial balance · Ledger',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/trial-balance').then((module) => module.TrialBalancePage),
      },
      {
        path: 'accounts',
        title: 'Accounts · Ledger',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/accounts').then((module) => module.AccountsPage),
      },
      {
        path: 'accounts/:id',
        title: 'Account statement · Ledger',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/account-statement').then((module) => module.AccountStatementPage),
      },
      {
        path: 'journal/new',
        title: 'New journal entry · Ledger',
        canActivate: [holding('Approver')],
        loadComponent: () =>
          import('./pages/journal-entry').then((module) => module.JournalEntryPage),
      },
      {
        path: 'receivables/aged',
        title: 'Aged receivables · Ledger',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/aged-receivables').then((module) => module.AgedReceivablesPage),
      },
      {
        path: 'periods',
        title: 'Periods · Ledger',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/periods').then((module) => module.PeriodsPage),
      },
      {
        path: 'no-access',
        title: 'No access · Ledger',
        loadComponent: () => import('./pages/no-access').then((module) => module.NoAccess),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
