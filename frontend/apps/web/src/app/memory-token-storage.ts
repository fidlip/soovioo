import { Injectable } from '@angular/core';
import { TokenStorage } from '@soovioo/shared';

/** Where the access token lived before it moved to memory. */
const LEGACY_STORAGE_KEY = 'soovioo.auth.token';

/**
 * Web access-token storage: memory only. The token is short-lived (15 min)
 * and a reload gets a new one from the httpOnly refresh cookie
 * (`AuthApiService.restoreSession()` at startup), so nothing readable by
 * page script outlives the tab.
 */
@Injectable()
export class MemoryTokenStorage extends TokenStorage {
  private token: string | null = null;

  constructor() {
    super();
    // Drop the 8-hour token older builds left in localStorage.
    try {
      localStorage.removeItem(LEGACY_STORAGE_KEY);
    } catch {
      /* storage disabled — nothing to clean up */
    }
  }

  getToken(): string | null {
    return this.token;
  }

  setToken(token: string): void {
    this.token = token;
  }

  clearToken(): void {
    this.token = null;
  }
}
