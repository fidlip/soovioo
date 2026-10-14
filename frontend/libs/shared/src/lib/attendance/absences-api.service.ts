import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Absence, CreateAbsenceRequest } from './absence.model';

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

  submitExcuse(id: number, reason: string): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/absences/${id}/excuse`, { reason });
  }

  reviewAbsence(id: number, approve: boolean): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/absences/${id}/review`, { approve });
  }
}
