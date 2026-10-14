/**
 * Platform-specific token persistence. Web backs this with localStorage;
 * a NativeScript implementation (not wired up yet) would use its own
 * application-settings storage instead.
 */
export abstract class TokenStorage {
  abstract getToken(): string | null;
  abstract setToken(token: string): void;
  abstract clearToken(): void;
}
