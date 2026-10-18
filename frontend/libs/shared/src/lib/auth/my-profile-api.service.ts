import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { AuthApiService } from './auth-api.service';
import { TokenStorage } from './token-storage';
import {
  ChangePasswordRequest,
  ChangePasswordResponse,
  MyProfile,
  UpdateMyProfileRequest,
} from './my-profile.model';

@Injectable({ providedIn: 'root' })
export class MyProfileApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly tokenStorage = inject(TokenStorage);
  private readonly authApi = inject(AuthApiService);

  getProfile(): Observable<MyProfile> {
    return this.http.get<MyProfile>(`${this.baseUrl}/api/users/me`);
  }

  updateProfile(request: UpdateMyProfileRequest): Observable<MyProfile> {
    return this.http.put<MyProfile>(`${this.baseUrl}/api/users/me`, request);
  }

  /** The response carries a freshly signed token — the one the caller is
   * still holding has `mustChangePassword` baked in from before this call,
   * see backend `MyProfileController.ChangePassword`. A new password also
   * revokes every other session and starts a new one for this caller, hence
   * the refresh cookie/token handling. */
  changePassword(
    request: ChangePasswordRequest,
  ): Observable<ChangePasswordResponse> {
    return this.http
      .post<ChangePasswordResponse>(
        `${this.baseUrl}/api/users/me/change-password`,
        { ...request, refreshToken: this.tokenStorage.getRefreshToken() },
        this.authApi.cookieOptions(),
      )
      .pipe(tap((response) => this.authApi.store(response)));
  }
}
