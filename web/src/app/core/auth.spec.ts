import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { Auth, authInterceptor, isLive, nameFromToken } from './auth';

function token(claims: object): string {
  const encode = (value: object) =>
    btoa(JSON.stringify(value)).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
  return `${encode({ alg: 'HS256' })}.${encode(claims)}.signature`;
}

const inAnHour = () => new Date(Date.now() + 3_600_000).toISOString();

describe('Auth', () => {
  let auth: Auth;
  let http: HttpClient;
  let server: HttpTestingController;
  let router: Router;

  beforeEach(() => {
    sessionStorage.clear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        provideRouter([]),
      ],
    });
    auth = TestBed.inject(Auth);
    http = TestBed.inject(HttpClient);
    server = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
  });

  afterEach(() => server.verify());

  function signIn(roles: string[]): void {
    auth.signIn('amira', 'a-password').subscribe();
    const request = server.expectOne('/api/auth/token');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({ accessToken: token({ name: 'amira' }), expiresAt: inAnHour(), roles });
  }

  it('reads the user name out of the token', () => {
    expect(nameFromToken(token({ name: 'amira' }))).toBe('amira');
    expect(nameFromToken('not-a-token')).toBe('');
  });

  it('treats a session past its expiry as gone', () => {
    const session = { token: 't', name: 'amira', roles: [], expiresAt: '2026-10-03T12:00:00Z' };

    expect(isLive(session, Date.parse('2026-10-03T11:59:59Z'))).toBe(true);
    expect(isLive(session, Date.parse('2026-10-03T12:00:00Z'))).toBe(false);
    expect(isLive(null, 0)).toBe(false);
  });

  it('holds the roles the server returned and nothing else', () => {
    signIn(['Clerk']);

    expect(auth.user()?.name).toBe('amira');
    expect(auth.has('Clerk')).toBe(true);
    expect(auth.has('Approver', 'Controller')).toBe(false);
  });

  it('sends the token to the api and not to other hosts', () => {
    signIn(['Viewer']);

    http.get('/api/accounts').subscribe();
    http.get('https://example.com/data').subscribe();

    expect(server.expectOne('/api/accounts').request.headers.get('Authorization')).toMatch(
      /^Bearer /,
    );
    expect(server.expectOne('https://example.com/data').request.headers.has('Authorization')).toBe(
      false,
    );
  });

  it('signs out and returns to the sign-in page when the api answers 401', () => {
    const navigate = vi.spyOn(router, 'navigate').mockResolvedValue(true);
    signIn(['Viewer']);

    http.get('/api/accounts').subscribe({ error: () => undefined });
    server.expectOne('/api/accounts').flush(null, { status: 401, statusText: 'Unauthorized' });

    expect(auth.user()).toBeNull();
    expect(sessionStorage.getItem('ledger.session')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/sign-in']);
  });

  it('keeps the session when the api answers 403', () => {
    signIn(['Viewer']);

    http.post('/api/journal-entries', {}).subscribe({ error: () => undefined });
    server.expectOne('/api/journal-entries').flush(null, { status: 403, statusText: 'Forbidden' });

    expect(auth.user()?.name).toBe('amira');
  });
});
