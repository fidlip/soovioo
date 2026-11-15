import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  SchoolDayOffRequest,
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

  /** The jarní prázdniny of the `district`, by default the school's. */
  getSuggestion(district?: string | null): Observable<SchoolYearSuggestion> {
    return this.http.get<SchoolYearSuggestion>(
      `${this.baseUrl}/api/school-years/suggestion`,
      { params: district ? { district } : {} },
    );
  }

  /** The okresy the jarní prázdniny go by, alphabetically. */
  getDistricts(): Observable<string[]> {
    return this.http.get<string[]>(
      `${this.baseUrl}/api/school-years/districts`,
    );
  }

  getSpringHolidays(
    startYear: number,
    district: string,
  ): Observable<SchoolDayOffRequest> {
    return this.http.get<SchoolDayOffRequest>(
      `${this.baseUrl}/api/school-years/spring-holidays`,
      { params: { startYear, district } },
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
