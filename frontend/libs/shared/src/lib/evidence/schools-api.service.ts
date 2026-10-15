import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateSchoolRequest, School } from './school.model';

/** SuperAdmin-only — tenant management, see SchoolsController. */
@Injectable({ providedIn: 'root' })
export class SchoolsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getSchools(): Observable<School[]> {
    return this.http.get<School[]>(`${this.baseUrl}/api/schools`);
  }

  addSchool(request: CreateSchoolRequest): Observable<School> {
    return this.http.post<School>(`${this.baseUrl}/api/schools`, request);
  }
}
