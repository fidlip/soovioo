import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  SchoolDirectory,
  SchoolDirectoryQuery,
} from './school-directory.model';

@Injectable({ providedIn: 'root' })
export class SchoolDirectoryApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getSchoolDirectory(query: SchoolDirectoryQuery): Observable<SchoolDirectory> {
    let params = new HttpParams()
      .set('page', query.page)
      .set('pageSize', query.pageSize);
    if (query.search?.trim()) {
      params = params.set('search', query.search.trim());
    }
    if (query.kind) {
      params = params.set('kind', query.kind);
    }
    if (query.classId != null) {
      params = params.set('classId', query.classId);
    }
    return this.http.get<SchoolDirectory>(
      `${this.baseUrl}/api/school-directory`,
      { params },
    );
  }
}
