import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  SchoolYear,
  SchoolYearRequest,
  SchoolYearSuggestion,
} from './school-year.model';

@Injectable({ providedIn: 'root' })
export class SchoolYearsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Oldest first. */
  getSchoolYears(): Observable<SchoolYear[]> {
    return this.http.get<SchoolYear[]>(`${this.baseUrl}/api/school-years`);
  }

  getSuggestion(): Observable<SchoolYearSuggestion> {
    return this.http.get<SchoolYearSuggestion>(
      `${this.baseUrl}/api/school-years/suggestion`,
    );
  }

  create(request: SchoolYearRequest): Observable<SchoolYear> {
    return this.http.post<SchoolYear>(
      `${this.baseUrl}/api/school-years`,
      request,
    );
  }

  update(id: number, request: SchoolYearRequest): Observable<SchoolYear> {
    return this.http.put<SchoolYear>(
      `${this.baseUrl}/api/school-years/${id}`,
      request,
    );
  }
}
