import { Injectable } from '@angular/core';
import { ApplicationSettings } from '@nativescript/core';
import { TokenStorage } from '@soovioo/shared';

const STORAGE_KEY = 'soovioo.auth.token';

@Injectable()
export class NativeTokenStorage implements TokenStorage {
  getToken(): string | null {
    return ApplicationSettings.getString(STORAGE_KEY, '') || null;
  }

  setToken(token: string): void {
    ApplicationSettings.setString(STORAGE_KEY, token);
  }

  clearToken(): void {
    ApplicationSettings.remove(STORAGE_KEY);
  }
}
