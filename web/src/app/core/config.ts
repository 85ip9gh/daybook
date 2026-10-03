import { Injectable } from '@angular/core';

export interface DemoAccount {
  userName: string;
  role: string;
  summary: string;
}

export interface Demo {
  password: string;
  accounts: DemoAccount[];
}

interface ConfigFile {
  apiBase?: string;
  demo?: Partial<Demo>;
}

function readDemo(demo: Partial<Demo> | undefined): Demo | null {
  if (!demo || typeof demo.password !== 'string' || !Array.isArray(demo.accounts)) {
    return null;
  }

  const accounts = demo.accounts.filter(
    (account) =>
      typeof account?.userName === 'string' &&
      typeof account.role === 'string' &&
      typeof account.summary === 'string',
  );
  return accounts.length > 0 ? { password: demo.password, accounts } : null;
}

@Injectable({ providedIn: 'root' })
export class AppConfig {
  private base = '/api';
  private demoAccess: Demo | null = null;

  get apiBase(): string {
    return this.base;
  }

  get demo(): Demo | null {
    return this.demoAccess;
  }

  async load(): Promise<void> {
    try {
      const response = await fetch('config.json', { cache: 'no-store' });
      if (response.ok) {
        this.apply((await response.json()) as ConfigFile);
      }
    } catch {
      this.apply({});
    }
  }

  apply(file: ConfigFile): void {
    this.base = (file.apiBase ?? '/api').replace(/\/+$/, '');
    this.demoAccess = readDemo(file.demo);
  }
}
