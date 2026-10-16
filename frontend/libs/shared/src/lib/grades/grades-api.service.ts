import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateGradeRequest, Grade } from './grade.model';

@Injectable({ providedIn: 'root' })
export class GradesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getGrades(): Observable<Grade[]> {
    return this.http.get<Grade[]>(`${this.baseUrl}/api/grades`);
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
