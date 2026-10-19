import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateSuperAdminRequest,
  SuperAdmin,
  SupportSession,
  SupportSessionStart,
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

  /** Sent with the support token itself. */
  endCurrentSession(): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/support-sessions/current/end`,
      {},
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

  /** Rejected for yourself and for the last SuperAdmin. */
  deleteSuperAdmin(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/superadmins/${id}`);
  }
}
