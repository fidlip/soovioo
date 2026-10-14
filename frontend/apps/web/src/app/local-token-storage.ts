import { Injectable } from '@angular/core';
import { TokenStorage } from '@sova/shared';

const STORAGE_KEY = 'sova.auth.token';

@Injectable()
export class LocalTokenStorage implements TokenStorage {
  getToken(): string | null {
    try {
      return localStorage.getItem(STORAGE_KEY);
    } catch {
      return null;
    }
  }

  setToken(token: string): void {
    try {
      localStorage.setItem(STORAGE_KEY, token);
    } catch {
      /* ignore — e.g. storage disabled */
    }
  }

  clearToken(): void {
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch {
      /* ignore */
    }
  }
}
