import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { PagedResult } from '../common/paged-result.model';
import {
  CreateSchoolRequest,
  School,
  UpdateSchoolRequest,
} from './school.model';

/** SuperAdmin-only — tenant management, see SchoolsController. */
@Injectable({ providedIn: 'root' })
export class SchoolsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** search matches name or slug (case-insensitive substring), filtered
   * server-side alongside pagination. */
  getSchools(
    page = 1,
    pageSize = 20,
    search?: string,
    demos = false,
  ): Observable<PagedResult<School>> {
    return this.http.get<PagedResult<School>>(`${this.baseUrl}/api/schools`, {
      params: {
        page,
        pageSize,
        ...(search ? { search } : {}),
        ...(demos ? { demos } : {}),
      },
    });
  }

  addSchool(request: CreateSchoolRequest): Observable<School> {
    return this.http.post<School>(`${this.baseUrl}/api/schools`, request);
  }

  updateSchool(id: number, request: UpdateSchoolRequest): Observable<School> {
    return this.http.put<School>(`${this.baseUrl}/api/schools/${id}`, request);
  }

  /** Rejected by the backend (400) unless the school has no users at all —
   * see SchoolsController.DeleteSchool. */
  deleteSchool(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/schools/${id}`);
  }
}
