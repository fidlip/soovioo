import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolPeriod, UpsertSchoolPeriodRequest } from './school-period.model';

@Injectable({ providedIn: 'root' })
export class SchoolPeriodsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getPeriods(): Observable<SchoolPeriod[]> {
    return this.http.get<SchoolPeriod[]>(`${this.baseUrl}/api/school-periods`);
  }

  upsertPeriod(periodNumber: number, request: UpsertSchoolPeriodRequest): Observable<SchoolPeriod> {
    return this.http.put<SchoolPeriod>(`${this.baseUrl}/api/school-periods/${periodNumber}`, request);
  }

  deletePeriod(periodNumber: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/school-periods/${periodNumber}`);
  }
}
