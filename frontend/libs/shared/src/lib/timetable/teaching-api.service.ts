import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CurriculumItem, TeachingOverview } from './teaching.model';

/** Every change answers with the whole overview, checks recomputed. */
@Injectable({ providedIn: 'root' })
export class TeachingApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private url(versionId: number): string {
    return `${this.baseUrl}/api/timetable-versions/${versionId}`;
  }

  getTeaching(versionId: number): Observable<TeachingOverview> {
    return this.http.get<TeachingOverview>(`${this.url(versionId)}/teaching`);
  }

  saveCurriculum(
    versionId: number,
    items: CurriculumItem[],
  ): Observable<TeachingOverview> {
    return this.http.put<TeachingOverview>(
      `${this.url(versionId)}/curriculum`,
      {
        items,
      },
    );
  }

  /** `teacherId` null = chosen automatically. */
  setAssignment(
    versionId: number,
    schoolClassId: number,
    subjectId: number,
    teacherId: number | null,
  ): Observable<TeachingOverview> {
    return this.http.put<TeachingOverview>(
      `${this.url(versionId)}/assignments`,
      { schoolClassId, subjectId, teacherId },
    );
  }

  confirmAssignments(versionId: number): Observable<TeachingOverview> {
    return this.http.post<TeachingOverview>(
      `${this.url(versionId)}/assignments/confirm`,
      {},
    );
  }

  importFromTimetable(versionId: number): Observable<TeachingOverview> {
    return this.http.post<TeachingOverview>(
      `${this.url(versionId)}/teaching/import`,
      {},
    );
  }
}
