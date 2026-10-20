import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolType } from './student-term';

/** What anyone may know about a school before logging in. */
export interface PublicSchool {
  name: string;
  type: SchoolType;
}

/** Anonymous — see PublicSchoolsController. */
@Injectable({ providedIn: 'root' })
export class PublicSchoolsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** 404 when no school has this slug. */
  getSchool(slug: string): Observable<PublicSchool> {
    return this.http.get<PublicSchool>(
      `${this.baseUrl}/api/public/schools/${encodeURIComponent(slug)}`,
    );
  }
}
