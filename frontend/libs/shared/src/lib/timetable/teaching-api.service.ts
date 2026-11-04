import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CurriculumItem,
  TeacherTeaching,
  TeachingOverview,
} from './teaching.model';

/** Every change answers with the whole overview, checks recomputed. */
@Injectable({ providedIn: 'root' })
export class TeachingApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private url(versionId: number): string {
    return `${this.baseUrl}/api/timetable-versions/${versionId}`;
  }

  /** The version valid today unless `versionId`; null = not a teacher there. */
  getTeacherTeaching(
    teacherId: number,
    versionId?: number | null,
  ): Observable<TeacherTeaching | null> {
    return this.http.get<TeacherTeaching | null>(
      `${this.baseUrl}/api/teaching/teachers/${teacherId}`,
      { params: versionId ? { versionId } : {} },
    );
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

  /** `hoursPerWeek` null = as the class's grade, 0 = not in the class. */
  /** null = as the grade. */
  setClassDoubleLessons(
    versionId: number,
    schoolClassId: number,
    subjectId: number,
    doubleLessons: boolean | null,
  ): Observable<TeachingOverview> {
    return this.http.put<TeachingOverview>(
      `${this.url(versionId)}/class-double-lessons`,
      { schoolClassId, subjectId, doubleLessons },
    );
  }

  setClassCurriculum(
    versionId: number,
    schoolClassId: number,
    subjectId: number,
    hoursPerWeek: number | null,
  ): Observable<TeachingOverview> {
    return this.http.put<TeachingOverview>(
      `${this.url(versionId)}/class-curriculum`,
      { schoolClassId, subjectId, hoursPerWeek },
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
