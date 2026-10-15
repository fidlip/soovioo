import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateSubjectRequest, Subject } from './subject.model';

@Injectable({ providedIn: 'root' })
export class SubjectsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getSubjects(): Observable<Subject[]> {
    return this.http.get<Subject[]>(`${this.baseUrl}/api/subjects`);
  }

  addSubject(request: CreateSubjectRequest): Observable<Subject> {
    return this.http.post<Subject>(`${this.baseUrl}/api/subjects`, request);
  }
}
