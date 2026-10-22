import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { ClassDirectory } from './class-directory.model';

@Injectable({ providedIn: 'root' })
export class ClassDirectoryApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** `studentId` omitted = the caller's first child (or the pupil themselves). */
  getClassDirectory(studentId?: number): Observable<ClassDirectory> {
    const params =
      studentId === undefined
        ? undefined
        : new HttpParams().set('studentId', studentId);
    return this.http.get<ClassDirectory>(
      `${this.baseUrl}/api/class-directory`,
      { params },
    );
  }
}
