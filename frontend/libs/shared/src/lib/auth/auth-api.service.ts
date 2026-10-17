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
    return this.getClaim(
      'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
      'role',
    );
  }

  /** Username claim from the stored JWT, or null if not logged in / unparseable. */
  getUsername(): string | null {
    return this.getClaim(
      'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
      'unique_name',
    );
  }

  private getClaim(...keys: string[]): string | null {
    const token = this.tokenStorage.getToken();
    if (!token) {
      return null;
    }
    try {
      const payload = JSON.parse(atob(token.split('.')[1]));
      for (const key of keys) {
        if (payload[key] != null) {
          return payload[key];
        }
      }
      return null;
    } catch {
      return null;
    }
  }

  /** True for Teacher/Admin — the roles allowed to write grades/absences/timetable/messages. */
  isTeacherOrAdmin(): boolean {
    const role = this.getRole();
    return role === 'Teacher' || role === 'Admin';
  }

  /** True for Teacher specifically — e.g. the Rozvrh page defaults a
   * Teacher's view to their own schedule, which doesn't apply to Admin. */
  isTeacher(): boolean {
    return this.getRole() === 'Teacher';
  }

  /** True for Admin — the only role allowed to manage users/classes/subjects/parent links. */
  isAdmin(): boolean {
    return this.getRole() === 'Admin';
  }

  /** True for SuperAdmin — manages schools themselves, not any one school's data. */
  isSuperAdmin(): boolean {
    return this.getRole() === 'SuperAdmin';
  }

  /** True for Student — a student logging in for themselves. Read-only,
   * same agendas as Parent, but unlike Parent never allowed to submit their
   * own excuse (see `AbsencesController.SubmitExcuse` on the backend). */
  isStudent(): boolean {
    return this.getRole() === 'Student';
  }

  /** True when an Admin assigned this account's current password (new
   * account or reset) and its owner hasn't picked their own yet — forces a
   * redirect to Settings, see `auth.guard.ts`. */
  mustChangePassword(): boolean {
    return this.getClaim('mustChangePassword') === 'true';
  }
}
