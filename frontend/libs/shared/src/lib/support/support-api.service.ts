import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { LoginResponse } from '../auth/login-response.model';
import {
  CreateSuperAdminRequest,
  SuperAdmin,
  SupportSession,
  SupportSessionStart,
  UpdateSuperAdminRequest,
} from './support.model';

/** SuperAdmin support mode and SuperAdmin accounts — see SupportController. */
@Injectable({ providedIn: 'root' })
export class SupportApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  startSupportSession(schoolId: number): Observable<SupportSessionStart> {
    return this.http.post<SupportSessionStart>(
      `${this.baseUrl}/api/schools/${schoolId}/support-sessions`,
      {},
    );
  }

  /** On the school's page: one-time code → support-mode login (also sets
   * the support cookie that keeps it alive across reloads). */
  redeem(code: string, options: { withCredentials?: boolean } = {}): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(
      `${this.baseUrl}/api/support-sessions/redeem`,
      { code },
      options,
    );
  }

  /** Sent with the support token itself; also drops the support cookie. */
  endCurrentSession(
    options: { withCredentials?: boolean } = {},
  ): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/support-sessions/current/end`,
      {},
      options,
    );
  }

  getSupportSessions(): Observable<SupportSession[]> {
    return this.http.get<SupportSession[]>(
      `${this.baseUrl}/api/support-sessions`,
    );
  }

  getSuperAdmins(): Observable<SuperAdmin[]> {
    return this.http.get<SuperAdmin[]>(`${this.baseUrl}/api/superadmins`);
  }

  addSuperAdmin(request: CreateSuperAdminRequest): Observable<SuperAdmin> {
    return this.http.post<SuperAdmin>(
      `${this.baseUrl}/api/superadmins`,
      request,
    );
  }

  updateSuperAdmin(
    id: number,
    request: UpdateSuperAdminRequest,
  ): Observable<SuperAdmin> {
    return this.http.put<SuperAdmin>(
      `${this.baseUrl}/api/superadmins/${id}`,
      request,
    );
  }

  /** Rejected for yourself and for the last SuperAdmin. */
  deleteSuperAdmin(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/superadmins/${id}`);
  }
}
