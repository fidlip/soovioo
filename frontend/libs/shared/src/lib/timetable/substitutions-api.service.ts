import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  LessonChange,
  LessonChangeRequest,
  LessonChangeScope,
  SubstitutionCandidates,
  SubstitutionDay,
  TeacherAbsenceRequest,
} from './substitution.model';

@Injectable({ providedIn: 'root' })
export class SubstitutionsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private url(path: string): string {
    return `${this.baseUrl}/api/substitutions/${path}`;
  }

  /** The published changes of the week starting `weekStart`. */
  getWeek(
    weekStart: string,
    scope: LessonChangeScope,
  ): Observable<LessonChange[]> {
    let params = new HttpParams().set('weekStart', weekStart);
    for (const [key, value] of Object.entries(scope)) {
      if (value != null) {
        params = params.set(key, value);
      }
    }
    return this.http.get<LessonChange[]>(this.url('week'), { params });
  }

  getDay(date: string): Observable<SubstitutionDay> {
    return this.http.get<SubstitutionDay>(this.url(`days/${date}`));
  }

  addAbsence(request: TeacherAbsenceRequest): Observable<SubstitutionDay> {
    return this.http.post<SubstitutionDay>(this.url('absences'), request);
  }

  deleteAbsence(id: number): Observable<void> {
    return this.http.delete<void>(this.url(`absences/${id}`));
  }

  saveChange(request: LessonChangeRequest): Observable<SubstitutionDay> {
    return this.http.put<SubstitutionDay>(this.url('changes'), request);
  }

  deleteChange(id: number): Observable<SubstitutionDay> {
    return this.http.delete<SubstitutionDay>(this.url(`changes/${id}`));
  }

  getCandidates(
    date: string,
    period: number,
    lesson: { schoolClassId: number | null; studentGroupId: number | null },
    subjectId: number | null = null,
  ): Observable<SubstitutionCandidates> {
    let params = new HttpParams().set('date', date).set('period', period);
    if (lesson.schoolClassId !== null) {
      params = params.set('schoolClassId', lesson.schoolClassId);
    }
    if (lesson.studentGroupId !== null) {
      params = params.set('studentGroupId', lesson.studentGroupId);
    }
    if (subjectId !== null) {
      params = params.set('subjectId', subjectId);
    }
    return this.http.get<SubstitutionCandidates>(this.url('candidates'), {
      params,
    });
  }

  fill(date: string): Observable<{ day: SubstitutionDay; unfilled: number }> {
    return this.http.post<{ day: SubstitutionDay; unfilled: number }>(
      this.url(`days/${date}/fill`),
      {},
    );
  }

  publish(date: string): Observable<SubstitutionDay> {
    return this.http.post<SubstitutionDay>(
      this.url(`days/${date}/publish`),
      {},
    );
  }
}
