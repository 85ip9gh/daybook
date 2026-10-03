import { HttpClient, HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { Observable, catchError, map, throwError } from 'rxjs';
import { AppConfig } from './config';

export type Role = 'Viewer' | 'Clerk' | 'Approver' | 'Controller' | 'Admin';

export const READERS: Role[] = ['Viewer', 'Clerk', 'Approver', 'Controller'];

export interface Session {
  token: string;
  expiresAt: string;
  roles: Role[];
  name: string;
}

interface TokenResponse {
  accessToken: string;
  expiresAt: string;
  roles: Role[];
}

const storageKey = 'ledger.session';

export function nameFromToken(token: string): string {
  try {
    const payload = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const claims = JSON.parse(atob(payload.padEnd(Math.ceil(payload.length / 4) * 4, '='))) as {
      name?: string;
    };
    return claims.name ?? '';
  } catch {
    return '';
  }
}

export function isLive(session: Session | null, now: number): session is Session {
  return session !== null && Date.parse(session.expiresAt) > now;
}

function restore(): Session | null {
  try {
    const stored = sessionStorage.getItem(storageKey);
    const session = stored ? (JSON.parse(stored) as Session) : null;
    return isLive(session, Date.now()) ? session : null;
  } catch {
    return null;
  }
}

@Injectable({ providedIn: 'root' })
export class Auth {
  private readonly http = inject(HttpClient);
  private readonly config = inject(AppConfig);
  private readonly session = signal<Session | null>(restore());

  readonly user = computed(() => this.session());

  token(): string | null {
    const session = this.session();
    if (!isLive(session, Date.now())) {
      if (session) {
        this.signOut();
      }

      return null;
    }

    return session.token;
  }

  has(...roles: Role[]): boolean {
    const held = this.session()?.roles ?? [];
    return roles.some((role) => held.includes(role));
  }

  signIn(userName: string, password: string): Observable<void> {
    return this.http
      .post<TokenResponse>(`${this.config.apiBase}/auth/token`, { userName, password })
      .pipe(
        map((response) => {
          const session: Session = {
            token: response.accessToken,
            expiresAt: response.expiresAt,
            roles: response.roles,
            name: nameFromToken(response.accessToken) || userName,
          };
          sessionStorage.setItem(storageKey, JSON.stringify(session));
          this.session.set(session);
        }),
      );
  }

  signOut(): void {
    sessionStorage.removeItem(storageKey);
    this.session.set(null);
  }
}

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(Auth);
  const router = inject(Router);
  const base = inject(AppConfig).apiBase;

  if (!request.url.startsWith(base) || request.url.endsWith('/auth/token')) {
    return next(request);
  }

  const token = auth.token();
  const outgoing = token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` } })
    : request;

  return next(outgoing).pipe(
    catchError((error: HttpErrorResponse) => {
      if (error.status === 401) {
        auth.signOut();
        void router.navigate(['/sign-in']);
      }

      return throwError(() => error);
    }),
  );
};

export const signedIn: CanActivateFn = () => {
  const auth = inject(Auth);
  return auth.token() ? true : inject(Router).createUrlTree(['/sign-in']);
};

export function holding(...roles: Role[]): CanActivateFn {
  return () => (inject(Auth).has(...roles) ? true : inject(Router).createUrlTree(['/no-access']));
}
