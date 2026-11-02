import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateTimetableVersionRequest,
  TimetableVersion,
  UpdateTimetableVersionRequest,
} from './timetable-version.model';

@Injectable({ providedIn: 'root' })
export class TimetableVersionsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/api/timetable-versions`;
  }

  getVersions(): Observable<TimetableVersion[]> {
    return this.http.get<TimetableVersion[]>(this.url);
  }

  createVersion(
    request: CreateTimetableVersionRequest,
  ): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(this.url, request);
  }

  updateVersion(
    id: number,
    request: UpdateTimetableVersionRequest,
  ): Observable<TimetableVersion> {
    return this.http.put<TimetableVersion>(`${this.url}/${id}`, request);
  }

  publishVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/publish`, {});
  }

  archiveVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/archive`, {});
  }

  restoreVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/restore`, {});
  }
}
