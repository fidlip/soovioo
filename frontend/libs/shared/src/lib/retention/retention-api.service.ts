import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  EraseDueResult,
  FormerStudent,
  LeaveNotice,
  LeaveRequest,
  RetentionOverview,
  RetentionPolicy,
} from './retention.model';

/** Administrace → Archiv a výmaz údajů — see RetentionController. Admin only. */
@Injectable({ providedIn: 'root' })
export class RetentionApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getPolicy(): Observable<RetentionPolicy> {
    return this.http.get<RetentionPolicy>(
      `${this.baseUrl}/api/retention/policy`,
    );
  }

  setPolicy(policy: RetentionPolicy): Observable<RetentionPolicy> {
    return this.http.put<RetentionPolicy>(
      `${this.baseUrl}/api/retention/policy`,
      policy,
    );
  }

  getPeople(): Observable<RetentionOverview> {
    return this.http.get<RetentionOverview>(
      `${this.baseUrl}/api/retention/people`,
    );
  }

  /** Rejected (400) once anything of the pupil was erased. */
  setLeave(
    studentId: number,
    request: LeaveRequest,
  ): Observable<FormerStudent> {
    return this.http.put<FormerStudent>(
      `${this.baseUrl}/api/retention/students/${studentId}/leave`,
      request,
    );
  }

  cancelLeave(studentId: number, notices: LeaveNotice[]): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/retention/students/${studentId}/leave`,
      { body: { notices } },
    );
  }

  /** What is due — or, with `everything`, the whole profile now. Returns
   * null (204) when the pupil is gone. */
  eraseStudent(
    studentId: number,
    everything: boolean,
  ): Observable<FormerStudent | null> {
    return this.http.post<FormerStudent | null>(
      `${this.baseUrl}/api/retention/students/${studentId}/erase`,
      { everything },
    );
  }

  eraseDue(): Observable<EraseDueResult> {
    return this.http.post<EraseDueResult>(
      `${this.baseUrl}/api/retention/erase-due`,
      {},
    );
  }

  eraseStaff(userId: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/retention/staff/${userId}/erase`,
      {},
    );
  }
}
