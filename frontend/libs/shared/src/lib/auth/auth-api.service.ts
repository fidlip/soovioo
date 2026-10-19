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
import { AUTH_REFRESH_VIA_COOKIE, CSRF_HEADER } from './auth-session.config';
import { LoginRequest, LoginResponse } from './login-response.model';
import { TokenStorage } from './token-storage';
import { SchoolType } from '../evidence/student-term';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly tokenStorage = inject(TokenStorage);
  private readonly viaCookie = inject(AUTH_REFRESH_VIA_COOKIE);

  private inFlightRefresh: Observable<string | null> | null = null;
  private readonly sessionExpiredSubject = new Subject<void>();

  /** Fires when a request hit 401 and the session couldn't be refreshed
   * (refresh token expired or revoked) — the app sends the user to login. */
  readonly sessionExpired$ = this.sessionExpiredSubject.asObservable();

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(
        `${this.baseUrl}/api/auth/login`,
        { ...request, useCookie: this.viaCookie },
        this.cookieOptions(),
      )
      .pipe(tap((response) => this.store(response)));
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
      .pipe(tap((response) => this.store(response)));
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
        { refreshToken: this.tokenStorage.getRefreshToken() },
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
    // Fire-and-forget: the local session is over either way; this revokes
    // the refresh token server-side and drops the cookie.
    this.http
      .post(
        `${this.baseUrl}/api/auth/logout`,
        { refreshToken },
        this.cookieOptions(),
      )
      .subscribe({ error: () => undefined });
  }

  isLoggedIn(): boolean {
    return !!this.tokenStorage.getToken();
  }

  /** Role claim from the stored JWT, or null if not logged in / unparseable. */
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

  /** Every role the account may switch between (`roles` claim) — the
   * active one is `getRole()`. */
  getRoles(): string[] {
    return this.getClaim('roles')?.split(',').filter(Boolean) ?? [];
  }

  /** Makes another of `getRoles()` the active one — the backend remembers
   * it and hands back an access token carrying it. */
  switchRole(role: string): Observable<void> {
    return this.http
      .post<{ token: string }>(`${this.baseUrl}/api/users/me/active-role`, {
        role,
      })
      .pipe(map((response) => this.store(response)));
  }

  /** ZŠ/SŠ of the user's school (`schoolType` claim) — drives "žák" vs.
   * "student" in the UI, see `localizeStudentTerm`. Null for SuperAdmin. */
  getSchoolType(): SchoolType | null {
    const value = this.getClaim('schoolType');
    return value === 'Elementary' || value === 'Secondary' ? value : null;
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

  /** True for Teacher/Admin — the roles allowed to write grades/absences/timetable/messages. */
  isTeacherOrAdmin(): boolean {
    const role = this.getRole();
    return role === 'Teacher' || role === 'Admin';
  }

  /** True for Teacher specifically — e.g. the Rozvrh page defaults a
   * Teacher's view to their own schedule, which doesn't apply to Admin. */
  isTeacher(): boolean {
    return this.getRole() === 'Teacher';
  }

  /** True for Admin — the only role allowed to manage users/classes/subjects/parent links. */
  isAdmin(): boolean {
    return this.getRole() === 'Admin';
  }

  /** True for SuperAdmin — manages schools themselves, not any one school's data. */
  isSuperAdmin(): boolean {
    return this.getRole() === 'SuperAdmin';
  }

  /** True for Student — a student logging in for themselves. Read-only,
   * same agendas as Parent, but unlike Parent never allowed to submit their
   * own excuse (see `AbsencesController.SubmitExcuse` on the backend). */
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
  const binary = atob(base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '='));
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}
