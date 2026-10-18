/**
 * Platform-specific token persistence.
 *
 * - Access token (15 min JWT): web keeps it in memory only, so an XSS can't
 *   lift a long-lived credential out of localStorage; mobile persists it.
 * - Refresh token: only body-delivery clients (mobile) ever see it. The web
 *   app's lives in an httpOnly cookie, so the defaults below are no-ops.
 */
export abstract class TokenStorage {
  abstract getToken(): string | null;
  abstract setToken(token: string): void;
  abstract clearToken(): void;

  getRefreshToken(): string | null {
    return null;
  }

  setRefreshToken(_token: string): void {
    // Cookie-based platforms never hold the refresh token.
  }

  clearRefreshToken(): void {
    // Cookie-based platforms never hold the refresh token.
  }
}
