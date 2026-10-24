import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { MyChild } from './my-child.model';
import { SetVisibilityRequest } from './visibility.model';

@Injectable({ providedIn: 'root' })
export class MyChildrenApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getMyChildren(): Observable<MyChild[]> {
    return this.http.get<MyChild[]>(`${this.baseUrl}/api/my-children`);
  }

  setVisibility(
    studentId: number,
    request: SetVisibilityRequest,
  ): Observable<MyChild[]> {
    return this.http.put<MyChild[]>(
      `${this.baseUrl}/api/my-children/${studentId}/visibility`,
      request,
    );
  }
}
