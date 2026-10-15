import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { LoginRequest, LoginResponse } from './login-response.model';
import { TokenStorage } from './token-storage';

@Injectable({ providedIn: 'root' })
export class AuthApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly tokenStorage = inject(TokenStorage);

  login(request: LoginRequest): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(`${this.baseUrl}/api/auth/login`, request)
      .pipe(tap((response) => this.tokenStorage.setToken(response.token)));
  }

  logout(): void {
    this.tokenStorage.clearToken();
  }

  isLoggedIn(): boolean {
    return !!this.tokenStorage.getToken();
  }

  /** Role claim from the stored JWT, or null if not logged in / unparseable. */
  getRole(): string | null {
    const token = this.tokenStorage.getToken();
    if (!token) {
      return null;
    }
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      return (
        payload['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ??
        payload['role'] ??
        null
      );
    } catch {
      return null;
    }
  }
}
