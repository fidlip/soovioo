import { Injectable } from '@angular/core';
import { ApplicationSettings } from '@nativescript/core';
import { TokenStorage } from '@soovioo/shared';

const STORAGE_KEY = 'soovioo.auth.token';
const REFRESH_STORAGE_KEY = 'soovioo.auth.refreshToken';

/** Mobile keeps both tokens in app settings — there's no httpOnly cookie
 * jar to hide the refresh token in, so the API hands it over in the
 * response body instead (see `AUTH_REFRESH_VIA_COOKIE`). */
@Injectable()
export class NativeTokenStorage extends TokenStorage {
  getToken(): string | null {
    return ApplicationSettings.getString(STORAGE_KEY, '') || null;
  }

  setToken(token: string): void {
    ApplicationSettings.setString(STORAGE_KEY, token);
  }

  clearToken(): void {
    ApplicationSettings.remove(STORAGE_KEY);
  }

  override getRefreshToken(): string | null {
    return ApplicationSettings.getString(REFRESH_STORAGE_KEY, '') || null;
  }

  override setRefreshToken(token: string): void {
    ApplicationSettings.setString(REFRESH_STORAGE_KEY, token);
  }

  override clearRefreshToken(): void {
    ApplicationSettings.remove(REFRESH_STORAGE_KEY);
  }
}
