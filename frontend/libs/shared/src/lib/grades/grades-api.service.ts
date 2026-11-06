import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { PagedResult } from '../common/paged-result.model';
import { CreateGradeRequest, Grade, GradesQuery } from './grade.model';

@Injectable({ providedIn: 'root' })
export class GradesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getGrades(query: GradesQuery = {}): Observable<PagedResult<Grade>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== undefined) {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PagedResult<Grade>>(`${this.baseUrl}/api/grades`, {
      params,
    });
  }

  addGrade(request: CreateGradeRequest): Observable<Grade> {
    return this.http.post<Grade>(`${this.baseUrl}/api/grades`, request);
  }

  updateGrade(id: number, request: CreateGradeRequest): Observable<Grade> {
    return this.http.put<Grade>(`${this.baseUrl}/api/grades/${id}`, request);
  }

  deleteGrade(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/grades/${id}`);
  }
}
