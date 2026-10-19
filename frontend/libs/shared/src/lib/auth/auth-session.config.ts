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

/**
 * Which school's page the app is on (web: from the URL path or subdomain),
 * sent with every session refresh so the API can continue a SuperAdmin's
 * support-mode session for that school. Null = no school (mobile, the
 * SuperAdmin area).
 */
export const CURRENT_SCHOOL_SLUG = new InjectionToken<() => string | null>(
  'CURRENT_SCHOOL_SLUG',
  { providedIn: 'root', factory: () => () => null },
);
