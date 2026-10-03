import { inject } from '@angular/core';
import { Routes } from '@angular/router';
import { Auth, READERS, holding, signedIn } from './core/auth';

export const routes: Routes = [
  {
    path: 'sign-in',
    title: 'Sign in · Daybook',
    loadComponent: () => import('./pages/sign-in').then((module) => module.SignIn),
  },
  {
    path: '',
    canActivate: [signedIn],
    loadComponent: () => import('./shell/shell').then((module) => module.Shell),
    children: [
      {
        path: '',
        pathMatch: 'full',
        redirectTo: () => {
          const auth = inject(Auth);
          return auth.has(...READERS)
            ? 'trial-balance'
            : auth.has('Admin')
              ? 'admin/users'
              : 'no-access';
        },
      },
      {
        path: 'trial-balance',
        title: 'Trial balance · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/trial-balance').then((module) => module.TrialBalancePage),
      },
      {
        path: 'accounts',
        title: 'Accounts · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/accounts').then((module) => module.AccountsPage),
      },
      {
        path: 'accounts/:id',
        title: 'Account statement · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/account-statement').then((module) => module.AccountStatementPage),
      },
      {
        path: 'journal/new',
        title: 'New journal entry · Daybook',
        canActivate: [holding('Approver')],
        loadComponent: () =>
          import('./pages/journal-entry').then((module) => module.JournalEntryPage),
      },
      {
        path: 'receivables/aged',
        title: 'Aged receivables · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/aged-receivables').then((module) => module.AgedReceivablesPage),
      },
      {
        path: 'receivables/customers',
        title: 'Customers · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/customers').then((module) => module.CustomersPage),
      },
      {
        path: 'receivables/customers/:id',
        title: 'Customer · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/customer').then((module) => module.CustomerPage),
      },
      {
        path: 'receivables/invoices',
        title: 'Invoices · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/invoices').then((module) => module.InvoicesPage),
      },
      {
        path: 'receivables/invoices/new',
        title: 'New invoice · Daybook',
        canActivate: [holding('Clerk')],
        loadComponent: () => import('./pages/invoice-new').then((module) => module.InvoiceNewPage),
      },
      {
        path: 'receivables/invoices/:id',
        title: 'Invoice · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/invoice').then((module) => module.InvoicePage),
      },
      {
        path: 'banking',
        title: 'Bank reconciliation · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () =>
          import('./pages/reconciliation').then((module) => module.ReconciliationPage),
      },
      {
        path: 'admin/users',
        title: 'Users · Daybook',
        canActivate: [holding('Admin')],
        loadComponent: () => import('./pages/users').then((module) => module.UsersPage),
      },
      {
        path: 'periods',
        title: 'Periods · Daybook',
        canActivate: [holding(...READERS)],
        loadComponent: () => import('./pages/periods').then((module) => module.PeriodsPage),
      },
      {
        path: 'no-access',
        title: 'No access · Daybook',
        loadComponent: () => import('./pages/no-access').then((module) => module.NoAccess),
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
