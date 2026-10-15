import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateSchoolClassRequest, SchoolClass } from './school-class.model';

@Injectable({ providedIn: 'root' })
export class SchoolClassesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getSchoolClasses(): Observable<SchoolClass[]> {
    return this.http.get<SchoolClass[]>(`${this.baseUrl}/api/school-classes`);
  }

  addSchoolClass(request: CreateSchoolClassRequest): Observable<SchoolClass> {
    return this.http.post<SchoolClass>(`${this.baseUrl}/api/school-classes`, request);
  }
}
