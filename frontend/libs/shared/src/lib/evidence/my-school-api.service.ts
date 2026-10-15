import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { School, SetSchoolMatrikaIdsRequest } from './school.model';

/** Admin self-service for their own school's settings — see MySchoolController. */
@Injectable({ providedIn: 'root' })
export class MySchoolApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getMySchool(): Observable<School> {
    return this.http.get<School>(`${this.baseUrl}/api/my-school`);
  }

  setMatrikaIds(request: SetSchoolMatrikaIdsRequest): Observable<School> {
    return this.http.put<School>(`${this.baseUrl}/api/my-school/matrika-ids`, request);
  }
}
