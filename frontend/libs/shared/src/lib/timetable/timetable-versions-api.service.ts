import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateTimetableVersionRequest,
  TimetableGeneration,
  TimetableAssessment,
  TimetableDropSlot,
  TimetableGenerationMode,
  TimetableUpdateMismatches,
  TimetableUpdateMoves,
  UnplacedLesson,
  TimetableVersion,
  UpdateTimetableVersionRequest,
} from './timetable-version.model';
import { DayOfWeek, WeekParity } from './timetable-entry.model';

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

  /** The draft waiting for that day, or a new copy of the version. */
  editFromDate(id: number, validFrom: string): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(
      `${this.url}/${id}/edit-from-date`,
      { validFrom },
    );
  }

  /** Classrooms taken at a slot of the version. */
  getBusyClassrooms(
    id: number,
    day: DayOfWeek,
    period: number,
    weekParity: WeekParity,
  ): Observable<number[]> {
    return this.http.get<number[]>(`${this.url}/${id}/busy-classrooms`, {
      params: { day, period, weekParity },
    });
  }

  /** The latest run; null when there's none. */
  getGeneration(id: number): Observable<TimetableGeneration | null> {
    return this.http.get<TimetableGeneration | null>(
      `${this.url}/${id}/generation`,
    );
  }

  /** The user saw how the latest run ended — its notification is read. */
  getUnplaced(id: number, schoolClassId: number): Observable<UnplacedLesson[]> {
    return this.http.get<UnplacedLesson[]>(`${this.url}/${id}/board/unplaced`, {
      params: { schoolClassId },
    });
  }

  /** Where a lesson in the timetable, or one of the unplaced, can go. */
  getDropSlots(
    id: number,
    lesson:
      | { entryId: number }
      | {
          schoolClassId: number;
          subjectId: number;
          groupId: number | null;
          weekParity: WeekParity;
        },
  ): Observable<TimetableDropSlot[]> {
    const params = Object.fromEntries(
      Object.entries(lesson).filter(([, value]) => value !== null),
    ) as Record<string, string | number>;
    return this.http.get<TimetableDropSlot[]>(`${this.url}/${id}/board/slots`, {
      params,
    });
  }

  moveLesson(
    id: number,
    entryId: number,
    day: DayOfWeek,
    period: number,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.url}/${id}/board/lessons/${entryId}/move`,
      { day, period },
    );
  }

  placeLesson(
    id: number,
    schoolClassId: number,
    lesson: UnplacedLesson,
    day: DayOfWeek,
    period: number,
  ): Observable<void> {
    return this.http.post<void>(`${this.url}/${id}/board/lessons`, {
      schoolClassId,
      subjectId: lesson.subjectId,
      groupId: lesson.groupId,
      weekParity: lesson.weekParity,
      day,
      period,
    });
  }

  unplaceLesson(id: number, entryId: number): Observable<void> {
    return this.http.delete<void>(`${this.url}/${id}/board/lessons/${entryId}`);
  }

  getAssessment(id: number): Observable<TimetableAssessment> {
    return this.http.get<TimetableAssessment>(
      `${this.url}/${id}/board/assessment`,
    );
  }

  generationSeen(id: number): Observable<void> {
    return this.http.post<void>(`${this.url}/${id}/generation/seen`, {});
  }

  generate(
    id: number,
    mode: TimetableGenerationMode,
    update?: {
      moves: TimetableUpdateMoves;
      mismatches: TimetableUpdateMismatches;
    },
  ): Observable<TimetableGeneration> {
    return this.http.post<TimetableGeneration>(`${this.url}/${id}/generation`, {
      mode,
      ...update,
    });
  }

  /** Those it concerns hear about it right away (null), or this many days
   * before it starts. */
  publishVersion(
    id: number,
    notifyDaysBefore: number | null = null,
  ): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/publish`, {
      notifyDaysBefore,
    });
  }

  /** What's wrong with the timetable — shown before publishing it. */
  getProblems(id: number): Observable<string[]> {
    return this.http.get<string[]>(`${this.url}/${id}/problems`);
  }

  /** A published version that doesn't apply yet back among drafts. */
  unpublishVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/unpublish`, {});
  }

  archiveVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/archive`, {});
  }

  restoreVersion(id: number): Observable<TimetableVersion> {
    return this.http.post<TimetableVersion>(`${this.url}/${id}/restore`, {});
  }
}
