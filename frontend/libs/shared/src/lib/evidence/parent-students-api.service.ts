import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateParentStudentLinkRequest, ParentStudentLink } from './parent-student-link.model';

@Injectable({ providedIn: 'root' })
export class ParentStudentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getLinks(): Observable<ParentStudentLink[]> {
    return this.http.get<ParentStudentLink[]>(`${this.baseUrl}/api/parent-student-links`);
  }

  addLink(request: CreateParentStudentLinkRequest): Observable<ParentStudentLink> {
    return this.http.post<ParentStudentLink>(`${this.baseUrl}/api/parent-student-links`, request);
  }

  removeLink(userId: number, studentId: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/parent-student-links/${userId}/${studentId}`);
  }
}
