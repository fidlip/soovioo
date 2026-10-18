import { InjectionToken } from '@angular/core';

/**
 * True on the web: the API delivers the refresh token as an httpOnly cookie
 * (requests that rely on it go `withCredentials` plus the CSRF header).
 * False (default) on mobile: the refresh token comes back in the response
 * body and is kept in `TokenStorage`.
 */
export const AUTH_REFRESH_VIA_COOKIE = new InjectionToken<boolean>(
  'AUTH_REFRESH_VIA_COOKIE',
  { providedIn: 'root', factory: () => false },
);

/** Must match `AuthSessionService.CsrfHeaderName` on the backend. */
export const CSRF_HEADER = 'X-Soovioo-Csrf';
