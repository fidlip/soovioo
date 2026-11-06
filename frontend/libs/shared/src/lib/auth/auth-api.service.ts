import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import {
  Observable,
  Subject,
  catchError,
  finalize,
  firstValueFrom,
  map,
  of,
  shareReplay,
  tap,
} from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  AUTH_REFRESH_VIA_COOKIE,
  CSRF_HEADER,
  CURRENT_SCHOOL_SLUG,
} from './auth-session.config';
import { LoginRequest, LoginResponse } from './login-response.model';
import { TokenStorage } from './token-storage';
import { SchoolType } from '../evidence/student-term';
import { Permission, PermissionScope, RoleRef } from './role.model';

/** This tab logged in, out or switched the role — what other tabs of the
 * same school are told (web), since they share its refresh cookie. */
export type SessionChange =
  | { kind: 'login'; username: string }
  | { kind: 'logout' }
  | { kind: 'role'; roleName: string };

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly tokenStorage = inject(TokenStorage);
  private readonly viaCookie = inject(AUTH_REFRESH_VIA_COOKIE);
  private readonly currentSchoolSlug = inject(CURRENT_SCHOOL_SLUG);

  private inFlightRefresh: Observable<string | null> | null = null;
  private readonly sessionExpiredSubject = new Subject<void>();
  private readonly sessionChangedSubject = new Subject<SessionChange>();

  /** Fires when a request hit 401 and the session couldn't be refreshed
   * (refresh token expired or revoked) — the app sends the user to login. */
  readonly sessionExpired$ = this.sessionExpiredSubject.asObservable();
  readonly sessionChanged$ = this.sessionChangedSubject.asObservable();

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(
        `${this.baseUrl}/api/auth/login`,
        { ...request, useCookie: this.viaCookie },
        this.cookieOptions(),
      )
      .pipe(tap((response) => this.startSession(response)));
  }

  /** Consumes an invite link's token, sets the account's password, and logs
   * the user in — same response shape as `login()`, for the same reason:
   * the invite flow ends with the user signed in, not back at a login form. */
  acceptInvite(token: string, newPassword: string): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(
        `${this.baseUrl}/api/auth/accept-invite`,
        { token, newPassword, useCookie: this.viaCookie },
        this.cookieOptions(),
      )
      .pipe(tap((response) => this.startSession(response)));
  }

  /** Logs in with the one-time code of switching from a linked account.
   * On mobile the account it replaces is logged out on the server too; on
   * the web that session stays at its school's address. */
  redeemLoginCode(code: string): Observable<LoginResponse> {
    const replaced = this.tokenStorage.getRefreshToken();
    return this.http
      .post<LoginResponse>(
        `${this.baseUrl}/api/auth/login-code`,
        { code, useCookie: this.viaCookie },
        this.cookieOptions(),
      )
      .pipe(
        tap((response) => {
          this.startSession(response);
          if (replaced) {
            this.http
              .post(`${this.baseUrl}/api/auth/logout`, {
                refreshToken: replaced,
              })
              .subscribe({ error: () => undefined });
          }
        }),
      );
  }

  /** Trades the refresh token for a new access token (rotating the refresh
   * token). Emits the new access token, or null when there's no live
   * session. Concurrent callers share one request — two parallel rotations
   * of the same token would look like token theft to the backend. */
  refresh(): Observable<string | null> {
    if (!this.viaCookie && !this.tokenStorage.getRefreshToken()) {
      return of(null);
    }
    this.inFlightRefresh ??= this.http
      .post<LoginResponse | null>(
        `${this.baseUrl}/api/auth/refresh`,
        {
          refreshToken: this.tokenStorage.getRefreshToken(),
          schoolSlug: this.currentSchoolSlug(),
        },
        this.cookieOptions(),
      )
      .pipe(
        map((response) => {
          // 204 = no refresh cookie at all, i.e. simply not logged in.
          if (!response) {
            this.clearTokens();
            return null;
          }
          this.store(response);
          return response.token;
        }),
        catchError(() => {
          this.clearTokens();
          return of(null);
        }),
        finalize(() => (this.inFlightRefresh = null)),
        shareReplay(1),
      );
    return this.inFlightRefresh;
  }

  /** Web app startup: the access token lives in memory only, so a reload
   * starts without one — restore it from the refresh cookie before the
   * router's guards run. Never rejects; no session just means logged out. */
  restoreSession(): Promise<unknown> {
    return firstValueFrom(this.refresh());
  }

  /** Called by the interceptor when a refresh after a 401 failed. */
  notifySessionExpired(): void {
    this.clearTokens();
    this.sessionExpiredSubject.next();
  }

  logout(): void {
    const refreshToken = this.tokenStorage.getRefreshToken();
    this.clearTokens();
    this.sessionChangedSubject.next({ kind: 'logout' });
    // Fire-and-forget: the local session is over either way; this revokes
    // the refresh token server-side and drops the cookie.
    this.http
      .post(
        `${this.baseUrl}/api/auth/logout`,
        { refreshToken, schoolSlug: this.currentSchoolSlug() },
        this.cookieOptions(),
      )
      .subscribe({ error: () => undefined });
  }

  isLoggedIn(): boolean {
    return !!this.tokenStorage.getToken();
  }

  /** Role claim from the stored JWT: the active role's kind (`Staff`,
   * `Parent`, `Student`) or `SuperAdmin`; null if not logged in. */
  getRole(): string | null {
    return this.getClaim(
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
      'role',
    );
  }

  /** Username claim from the stored JWT, or null if not logged in / unparseable. */
  getUsername(): string | null {
    return this.getClaim(
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
      'unique_name',
    );
  }

  /** Numeric user id claim from the stored JWT (ClaimTypes.NameIdentifier),
   * or null if not logged in / unparseable. */
  getUserId(): number | null {
    const raw = this.getClaim(
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier',
      'nameid',
    );
    const id = raw === null ? NaN : Number(raw);
    return Number.isNaN(id) ? null : id;
  }

  /** Every role the account may switch between (`roles` claim). */
  getRoles(): RoleRef[] {
    try {
      return JSON.parse(this.getClaim('roles') ?? '[]') as RoleRef[];
    } catch {
      return [];
    }
  }

  /** The active role (`roleId`/`roleName` claims); null for SuperAdmin. */
  getActiveRole(): { id: number; name: string } | null {
    const id = Number(this.getClaim('roleId'));
    return id ? { id, name: this.getClaim('roleName') ?? '' } : null;
  }

  /** Makes another of `getRoles()` the active one — the backend remembers
   * it and hands back an access token carrying it. */
  switchRole(roleId: number): Observable<void> {
    return this.http
      .post<{ token: string }>(`${this.baseUrl}/api/users/me/active-role`, {
        roleId,
      })
      .pipe(
        map((response) => {
          this.store(response);
          this.sessionChangedSubject.next({
            kind: 'role',
            roleName: this.getActiveRole()?.name ?? '',
          });
        }),
      );
  }

  /** How far the active role's permission reaches (`perms` claim). The
   * API checks for itself; this only decides what the UI offers. */
  scope(permission: Permission): PermissionScope {
    for (const pair of (this.getClaim('perms') ?? '').split(',')) {
      const [name, scope] = pair.split(':');
      if (name === permission) {
        return scope as PermissionScope;
      }
    }
    return 'None';
  }

  can(permission: Permission): boolean {
    return this.scope(permission) !== 'None';
  }

  /** ZŠ/SŠ of the user's school (`schoolType` claim) — drives "žák" vs.
   * "student" in the UI, see `localizeStudentTerm`. Null for SuperAdmin. */
  getSchoolType(): SchoolType | null {
    const value = this.getClaim('schoolType');
    return value === 'Elementary' || value === 'Secondary' ? value : null;
  }

  /** The name of the user's school (`schoolName` claim), null for SuperAdmin. */
  getSchoolName(): string | null {
    return this.getClaim('schoolName');
  }

  /** Set while a SuperAdmin acts in a school in support mode (token from
   * `SupportApiService.startSupportSession`): who and which school. */
  getSupportInfo(): { by: string; schoolName: string } | null {
    const by = this.getClaim('supportBy');
    return by ? { by, schoolName: this.getClaim('supportSchool') ?? '' } : null;
  }

  /** Request options for the endpoints that read or set the refresh cookie
   * (web only): send/accept cookies cross-origin, plus the CSRF header the
   * backend demands before it trusts a cookie-borne refresh token. */
  cookieOptions(): { withCredentials?: boolean; headers?: HttpHeaders } {
    return this.viaCookie
      ? {
          withCredentials: true,
          headers: new HttpHeaders({ [CSRF_HEADER]: '1' }),
        }
      : {};
  }

  /** Stores a freshly issued access token (and, on mobile, the rotated
   * refresh token) — also used after a password change. */
  store(response: { token: string; refreshToken?: string | null }): void {
    this.tokenStorage.setToken(response.token);
    if (response.refreshToken) {
      this.tokenStorage.setRefreshToken(response.refreshToken);
    }
  }

  /** Stores a new login's tokens and tells the app who logged in — also
   * for logins outside this service (the demo's user picker). */
  startSession(response: LoginResponse): void {
    this.store(response);
    this.sessionChangedSubject.next({
      kind: 'login',
      username: response.username,
    });
  }

  private clearTokens(): void {
    this.tokenStorage.clearToken();
    this.tokenStorage.clearRefreshToken();
  }

  private getClaim(...keys: string[]): string | null {
    const token = this.tokenStorage.getToken();
    if (!token) {
      return null;
    }
    try {
      const payload = JSON.parse(decodeJwtPart(token.split('.')[1]));
      for (const key of keys) {
        if (payload[key] != null) {
          return payload[key];
        }
      }
      return null;
    } catch {
      return null;
    }
  }

  /** Staff (any role of the kind) — vs. a parent or a pupil. */
  isStaff(): boolean {
    return this.getRole() === 'Staff';
  }

  /** Administrace in full: people, accounts, roles, the school's settings. */
  isAdmin(): boolean {
    return this.can('Administration');
  }

  /** True for SuperAdmin — manages schools themselves, not any one school's data. */
  isSuperAdmin(): boolean {
    return this.getRole() === 'SuperAdmin';
  }

  isParent(): boolean {
    return this.getRole() === 'Parent';
  }

  /** A pupil logging in for themselves. */
  isStudent(): boolean {
    return this.getRole() === 'Student';
  }

  /** True when an Admin assigned this account's current password (new
   * account or reset) and its owner hasn't picked their own yet — forces a
   * redirect to Settings, see `auth.guard.ts`. */
  mustChangePassword(): boolean {
    return this.getClaim('mustChangePassword') === 'true';
  }
}

/** base64url JWT segment -> string, decoding UTF-8 (plain `atob` would
 * turn "Škola" into mojibake). */
function decodeJwtPart(part: string): string {
  const base64 = part.replace(/-/g, '+').replace(/_/g, '/');
  const binary = atob(
    base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='),
  );
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
