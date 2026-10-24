import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { VisibilityRules } from '../people/visibility.model';
import {
  School,
  SetSchoolBankAccountRequest,
  SetSchoolDetailsRequest,
  SetSchoolMatrikaIdsRequest,
  SetSchoolTypeRequest,
} from './school.model';

/** Admin self-service for their own school's settings — see MySchoolController. */
@Injectable({ providedIn: 'root' })
export class MySchoolApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getMySchool(): Observable<School> {
    return this.http.get<School>(`${this.baseUrl}/api/my-school`);
  }

  setMatrikaIds(request: SetSchoolMatrikaIdsRequest): Observable<School> {
    return this.http.put<School>(
      `${this.baseUrl}/api/my-school/matrika-ids`,
      request,
    );
  }

  setType(request: SetSchoolTypeRequest): Observable<School> {
    return this.http.put<School>(`${this.baseUrl}/api/my-school/type`, request);
  }

  setDetails(request: SetSchoolDetailsRequest): Observable<School> {
    return this.http.put<School>(
      `${this.baseUrl}/api/my-school/details`,
      request,
    );
  }

  setBankAccount(request: SetSchoolBankAccountRequest): Observable<School> {
    return this.http.put<School>(
      `${this.baseUrl}/api/my-school/bank-account`,
      request,
    );
  }

  getVisibility(): Observable<VisibilityRules> {
    return this.http.get<VisibilityRules>(
      `${this.baseUrl}/api/my-school/visibility`,
    );
  }

  setVisibility(request: VisibilityRules): Observable<VisibilityRules> {
    return this.http.put<VisibilityRules>(
      `${this.baseUrl}/api/my-school/visibility`,
      request,
    );
  }
}
