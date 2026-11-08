import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  Absence,
  CreateAbsenceRequest,
  Excuse,
  ExcuseAttachment,
  ExcuseRequest,
  ExcuseResult,
} from './absence.model';

@Injectable({ providedIn: 'root' })
export class AbsencesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getAbsences(): Observable<Absence[]> {
    return this.http.get<Absence[]>(`${this.baseUrl}/api/absences`);
  }

  addAbsence(request: CreateAbsenceRequest): Observable<Absence> {
    return this.http.post<Absence>(`${this.baseUrl}/api/absences`, request);
  }

  updateAbsence(
    id: number,
    request: CreateAbsenceRequest,
  ): Observable<Absence> {
    return this.http.put<Absence>(
      `${this.baseUrl}/api/absences/${id}`,
      request,
    );
  }

  deleteAbsence(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/absences/${id}`);
  }

  /** A one-day omluvenka for the absence. */
  submitExcuse(id: number, reason: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/absences/${id}/excuse`, {
      reason,
    });
  }

  /** Rejecting needs `note`, the reason the family sees. */
  reviewAbsence(id: number, approve: boolean, note?: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/absences/${id}/review`, {
      approve,
      note,
    });
  }

  reopenAbsence(id: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/absences/${id}/reopen`,
      {},
    );
  }

  getExcuses(): Observable<Excuse[]> {
    return this.http.get<Excuse[]>(`${this.baseUrl}/api/excuses`);
  }

  createExcuse(request: ExcuseRequest): Observable<ExcuseResult> {
    return this.http.post<ExcuseResult>(`${this.baseUrl}/api/excuses`, request);
  }

  updateExcuse(id: number, request: ExcuseRequest): Observable<ExcuseResult> {
    return this.http.put<ExcuseResult>(
      `${this.baseUrl}/api/excuses/${id}`,
      request,
    );
  }

  deleteExcuse(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/excuses/${id}`);
  }

  /** Approves the waiting days up to today; rejecting needs `note`. */
  reviewExcuse(id: number, approve: boolean, note?: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/excuses/${id}/review`, {
      approve,
      note,
    });
  }

  reopenExcuse(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/excuses/${id}/reopen`, {});
  }

  uploadExcuseAttachment(id: number, file: File): Observable<ExcuseAttachment> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<ExcuseAttachment>(
      `${this.baseUrl}/api/excuses/${id}/attachments`,
      formData,
    );
  }

  deleteExcuseAttachment(id: number, attachmentId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/excuses/${id}/attachments/${attachmentId}`,
    );
  }

  downloadExcuseAttachment(id: number, attachmentId: number): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/api/excuses/${id}/attachments/${attachmentId}`,
      {
        responseType: 'blob',
      },
    );
  }
}
