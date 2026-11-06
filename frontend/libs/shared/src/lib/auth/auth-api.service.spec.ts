import {
  HttpClient,
  provideHttpClient,
  withInterceptors,
} from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api-base-url.token';
import { AuthApiService, SessionChange } from './auth-api.service';
import { AUTH_REFRESH_VIA_COOKIE, CSRF_HEADER } from './auth-session.config';
import { authInterceptor } from './auth.interceptor';
import { TokenStorage } from './token-storage';

class FakeTokenStorage extends TokenStorage {
  private token: string | null = null;
  private refreshToken: string | null = null;
  getToken() {
    return this.token;
  }
  setToken(token: string) {
    this.token = token;
  }
  clearToken() {
    this.token = null;
  }
  override getRefreshToken() {
    return this.refreshToken;
  }
  override setRefreshToken(token: string) {
    this.refreshToken = token;
  }
  override clearRefreshToken() {
    this.refreshToken = null;
  }
}

/** An unsigned JWT with these claims (the client only reads them). */
function jwt(claims: Record<string, unknown>): string {
  const part = (value: object) =>
    btoa(
      String.fromCharCode(...new TextEncoder().encode(JSON.stringify(value))),
    )
      .replace(/\+/g, '-')
      .replace(/\//g, '_')
      .replace(/=+$/, '');
  return `${part({ alg: 'none' })}.${part(claims)}.x`;
}

describe('AuthApiService', () => {
  let service: AuthApiService;
  let httpMock: HttpTestingController;
  let tokenStorage: FakeTokenStorage;

  function setup(viaCookie: boolean) {
    tokenStorage = new FakeTokenStorage();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: 'http://test' },
        { provide: TokenStorage, useValue: tokenStorage },
        { provide: AUTH_REFRESH_VIA_COOKIE, useValue: viaCookie },
      ],
    });
    service = TestBed.inject(AuthApiService);
    httpMock = TestBed.inject(HttpTestingController);
  }

  describe('redeemLoginCode', () => {
    const response = { token: jwt({}), username: 'novak' };

    it('on the web starts the session by the cookie and leaves the other one alone', () => {
      setup(true);
      const changes: SessionChange[] = [];
      service.sessionChanged$.subscribe((change) => changes.push(change));

      service.redeemLoginCode('abc').subscribe();

      const request = httpMock.expectOne('http://test/api/auth/login-code');
      expect(request.request.body).toEqual({ code: 'abc', useCookie: true });
      expect(request.request.withCredentials).toBe(true);
      expect(request.request.headers.has(CSRF_HEADER)).toBe(true);
      request.flush(response);
      expect(service.isLoggedIn()).toBe(true);
      expect(changes).toEqual([{ kind: 'login', username: 'novak' }]);
      httpMock.verify();
    });

    it('on mobile takes over the new tokens and logs the replaced account out on the server', () => {
      setup(false);
      tokenStorage.setRefreshToken('stary');

      service.redeemLoginCode('abc').subscribe();
      httpMock
        .expectOne('http://test/api/auth/login-code')
        .flush({ ...response, refreshToken: 'novy' });

      expect(tokenStorage.getRefreshToken()).toBe('novy');
      const logout = httpMock.expectOne('http://test/api/auth/logout');
      expect(logout.request.body).toEqual({ refreshToken: 'stary' });
      logout.flush(null);
      httpMock.verify();
    });

    it('a refused code leaves the session as it was', () => {
      setup(false);
      tokenStorage.setRefreshToken('stary');

      service.redeemLoginCode('abc').subscribe({ error: () => undefined });
      httpMock
        .expectOne('http://test/api/auth/login-code')
        .flush('ne', { status: 400, statusText: 'Bad Request' });

      expect(tokenStorage.getRefreshToken()).toBe('stary');
      httpMock.verify();
    });
  });

  it('reads the school name with its diacritics from the token', () => {
    setup(true);
    tokenStorage.setToken(jwt({ schoolName: 'SŠ Údolí' }));

    expect(service.getSchoolName()).toBe('SŠ Údolí');
  });

  afterEach(() => httpMock.verify());

  describe('mobile (refresh token in body)', () => {
    beforeEach(() => setup(false));

    it('stores both tokens after a successful login', () => {
      service.login({ username: 'ucitel', password: 'ucitel123' }).subscribe();
      const req = httpMock.expectOne('http://test/api/auth/login');
      expect(req.request.body.useCookie).toBe(false);
      expect(req.request.withCredentials).toBe(false);
      req.flush({
        token: 'abc',
        refreshToken: 'r1',
        username: 'ucitel',
        role: 'Teacher',
      });

      expect(tokenStorage.getToken()).toBe('abc');
      expect(tokenStorage.getRefreshToken()).toBe('r1');
      expect(service.isLoggedIn()).toBe(true);
    });

    it('clears tokens and revokes the refresh token on logout', () => {
      tokenStorage.setToken('abc');
      tokenStorage.setRefreshToken('r1');
      service.logout();

      const req = httpMock.expectOne('http://test/api/auth/logout');
      expect(req.request.body).toEqual({
        refreshToken: 'r1',
        schoolSlug: null,
      });
      req.flush(null, { status: 204, statusText: 'No Content' });
      expect(service.isLoggedIn()).toBe(false);
      expect(tokenStorage.getRefreshToken()).toBeNull();
    });

    it('tells who logged in and that they logged out', () => {
      const changes: SessionChange[] = [];
      service.sessionChanged$.subscribe((c) => changes.push(c));

      service.login({ username: 'ucitel', password: 'ucitel123' }).subscribe();
      httpMock.expectOne('http://test/api/auth/login').flush({
        token: 'abc',
        refreshToken: 'r1',
        username: 'ucitel',
        role: 'Staff',
      });
      service.logout();
      httpMock.expectOne('http://test/api/auth/logout').flush(null);

      expect(changes).toEqual([
        { kind: 'login', username: 'ucitel' },
        { kind: 'logout' },
      ]);
    });

    it('skips the refresh call entirely without a refresh token', () => {
      let result: string | null | undefined;
      service.refresh().subscribe((t) => (result = t));
      expect(result).toBeNull();
    });

    it('shares one refresh request between concurrent callers', () => {
      tokenStorage.setRefreshToken('r1');
      const results: (string | null)[] = [];
      service.refresh().subscribe((t) => results.push(t));
      service.refresh().subscribe((t) => results.push(t));

      httpMock.expectOne('http://test/api/auth/refresh').flush({
        token: 'new',
        refreshToken: 'r2',
        username: 'u',
        role: 'Teacher',
      });

      expect(results).toEqual(['new', 'new']);
      expect(tokenStorage.getRefreshToken()).toBe('r2');
    });

    it('retries a 401 once with a refreshed access token', () => {
      tokenStorage.setToken('stale');
      tokenStorage.setRefreshToken('r1');
      const http = TestBed.inject(HttpClient);
      let body: unknown;
      http.get('http://test/api/grades').subscribe((b) => (body = b));

      httpMock
        .expectOne('http://test/api/grades')
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      httpMock.expectOne('http://test/api/auth/refresh').flush({
        token: 'fresh',
        refreshToken: 'r2',
        username: 'u',
        role: 'Teacher',
      });
      const retry = httpMock.expectOne('http://test/api/grades');
      expect(retry.request.headers.get('Authorization')).toBe('Bearer fresh');
      retry.flush([1]);

      expect(body).toEqual([1]);
    });

    it('reports an expired session when the refresh after a 401 fails', () => {
      tokenStorage.setToken('stale');
      tokenStorage.setRefreshToken('revoked');
      const http = TestBed.inject(HttpClient);
      let expired = false;
      let failed = false;
      service.sessionExpired$.subscribe(() => (expired = true));
      http
        .get('http://test/api/grades')
        .subscribe({ error: () => (failed = true) });

      httpMock
        .expectOne('http://test/api/grades')
        .flush(null, { status: 401, statusText: 'Unauthorized' });
      httpMock
        .expectOne('http://test/api/auth/refresh')
        .flush(null, { status: 401, statusText: 'Unauthorized' });

      expect(failed).toBe(true);
      expect(expired).toBe(true);
      expect(service.isLoggedIn()).toBe(false);
    });
  });

  describe('web (refresh token in httpOnly cookie)', () => {
    beforeEach(() => setup(true));

    it('asks for the cookie on login and sends credentials', () => {
      service
        .login({ username: 'ucitel', password: 'ucitel123', rememberMe: true })
        .subscribe();
      const req = httpMock.expectOne('http://test/api/auth/login');
      expect(req.request.body).toEqual(
        expect.objectContaining({ useCookie: true, rememberMe: true }),
      );
      expect(req.request.withCredentials).toBe(true);
      req.flush({
        token: 'abc',
        refreshToken: null,
        username: 'ucitel',
        role: 'Teacher',
      });
      expect(tokenStorage.getToken()).toBe('abc');
    });

    it('restores the session on startup with the CSRF header', async () => {
      const restored = service.restoreSession();
      const req = httpMock.expectOne('http://test/api/auth/refresh');
      expect(req.request.withCredentials).toBe(true);
      expect(req.request.headers.get(CSRF_HEADER)).toBe('1');
      req.flush({ token: 'abc', username: 'ucitel', role: 'Teacher' });
      await restored;

      expect(service.isLoggedIn()).toBe(true);
    });

    it('treats 204 from refresh as simply logged out', async () => {
      const restored = service.restoreSession();
      httpMock
        .expectOne('http://test/api/auth/refresh')
        .flush(null, { status: 204, statusText: 'No Content' });
      await restored;

      expect(service.isLoggedIn()).toBe(false);
    });
  });

  describe('permissions from the token', () => {
    beforeEach(() => setup(false));

    const teacher = jwt({
      role: 'Staff',
      roleId: '5',
      roleName: 'Učitel',
      roles: JSON.stringify([
        { id: 5, name: 'Učitel', kind: 'Staff' },
        { id: 6, name: 'Rodič', kind: 'Parent' },
      ]),
      perms: 'GradesRead:MyClasses,GradesWrite:MyClasses,Students:ClassTeacher',
    });

    it('reads the scope of each permission, none when missing', () => {
      tokenStorage.setToken(teacher);

      expect(service.scope('GradesRead')).toBe('MyClasses');
      expect(service.scope('Students')).toBe('ClassTeacher');
      expect(service.scope('Timetable')).toBe('None');
      expect(service.can('GradesWrite')).toBe(true);
      expect(service.can('AuditLog')).toBe(false);
      expect(service.isAdmin()).toBe(false);
    });

    it('knows the kind, the active role and the ones to switch to', () => {
      tokenStorage.setToken(teacher);

      expect(service.isStaff()).toBe(true);
      expect(service.isParent()).toBe(false);
      expect(service.getActiveRole()).toEqual({ id: 5, name: 'Učitel' });
      expect(service.getRoles().map((r) => r.name)).toEqual([
        'Učitel',
        'Rodič',
      ]);
    });

    it('is the admin by the administration permission, not by a name', () => {
      tokenStorage.setToken(
        jwt({
          role: 'Staff',
          roleId: '1',
          roleName: 'Ředitelka',
          perms: 'Administration:School',
        }),
      );

      expect(service.isAdmin()).toBe(true);
    });

    it('allows nothing without a token or for a SuperAdmin', () => {
      expect(service.can('GradesRead')).toBe(false);
      expect(service.getRoles()).toEqual([]);

      tokenStorage.setToken(jwt({ role: 'SuperAdmin' }));
      expect(service.isSuperAdmin()).toBe(true);
      expect(service.can('Administration')).toBe(false);
      expect(service.getActiveRole()).toBeNull();
    });

    it('switches the active role by its id', () => {
      tokenStorage.setToken(teacher);
      service.switchRole(6).subscribe();

      const req = httpMock.expectOne('http://test/api/users/me/active-role');
      expect(req.request.body).toEqual({ roleId: 6 });
      req.flush({ token: jwt({ role: 'Parent', roleId: '6' }) });
      expect(service.isParent()).toBe(true);
    });

    it('refreshes the session in the background after a 403, so the permissions catch up', () => {
      tokenStorage.setToken(teacher);
      tokenStorage.setRefreshToken('r1');
      const http = TestBed.inject(HttpClient);
      let failed = false;
      http
        .get('http://test/api/grades')
        .subscribe({ error: () => (failed = true) });

      httpMock
        .expectOne('http://test/api/grades')
        .flush(null, { status: 403, statusText: 'Forbidden' });
      httpMock.expectOne('http://test/api/auth/refresh').flush({
        token: jwt({ role: 'Staff', roleId: '5', perms: '' }),
        refreshToken: 'r2',
        username: 'u',
        role: 'Staff',
      });

      expect(failed).toBe(true);
      expect(service.can('GradesRead')).toBe(false);
    });

    it('does not refresh after a 403 in support mode', () => {
      tokenStorage.setToken(jwt({ role: 'Staff', supportBy: 'superadmin' }));
      tokenStorage.setRefreshToken('r1');
      const http = TestBed.inject(HttpClient);
      http.get('http://test/api/grades').subscribe({ error: () => undefined });

      httpMock
        .expectOne('http://test/api/grades')
        .flush(null, { status: 403, statusText: 'Forbidden' });
      httpMock.expectNone('http://test/api/auth/refresh');
    });
  });
});
