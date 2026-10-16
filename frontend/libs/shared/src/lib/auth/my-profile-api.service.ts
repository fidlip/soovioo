import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  ChangePasswordRequest,
  MyProfile,
  UpdateMyProfileRequest,
} from './my-profile.model';

@Injectable({ providedIn: 'root' })
export class MyProfileApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getProfile(): Observable<MyProfile> {
    return this.http.get<MyProfile>(`${this.baseUrl}/api/users/me`);
  }

  updateProfile(request: UpdateMyProfileRequest): Observable<MyProfile> {
    return this.http.put<MyProfile>(`${this.baseUrl}/api/users/me`, request);
  }

  changePassword(request: ChangePasswordRequest): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/users/me/change-password`,
      request,
    );
  }
}
