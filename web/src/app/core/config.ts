import { Injectable } from '@angular/core';

interface ConfigFile {
  apiBase?: string;
}

@Injectable({ providedIn: 'root' })
export class AppConfig {
  private base = '/api';

  get apiBase(): string {
    return this.base;
  }

  async load(): Promise<void> {
    try {
      const response = await fetch('config.json', { cache: 'no-store' });
      if (response.ok) {
        const file = (await response.json()) as ConfigFile;
        this.base = (file.apiBase ?? this.base).replace(/\/+$/, '');
      }
    } catch {
      this.base = '/api';
    }
  }
}
