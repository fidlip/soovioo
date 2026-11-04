import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateTimetableEntryRequest,
  TeacherTimetableEntry,
  TimetableEntry,
} from './timetable-entry.model';

/** Which version to read: a given one, or the one valid on a date
 * ("yyyy-MM-dd"); neither = the one valid today. */
export interface TimetableVersionQuery {
  versionId?: number | null;
  date?: string | null;
}

/** A class's timetable; with `studentId` only the pupil's groups' lessons
 * besides the whole class's. */
export interface ClassTimetableQuery extends TimetableVersionQuery {
  studentId?: number | null;
}

function versionParams(query: TimetableVersionQuery): HttpParams {
  let params = new HttpParams();
  if (query.versionId != null) {
    params = params.set('versionId', query.versionId);
  }
  if (query.date) {
    params = params.set('date', query.date);
  }
  return params;
}

@Injectable({ providedIn: 'root' })
export class TimetableApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getTimetable(
    schoolClassId: number,
    query: ClassTimetableQuery = {},
  ): Observable<TimetableEntry[]> {
    let params = versionParams(query);
    if (query.studentId != null) {
      params = params.set('studentId', query.studentId);
    }
    return this.http.get<TimetableEntry[]>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable`,
      { params },
    );
  }

  /** A teacher's own schedule across every class they teach — unlike
   * getTimetable, not scoped to one class. */
  getTeacherTimetable(
    teacherId: number,
    query: TimetableVersionQuery = {},
  ): Observable<TeacherTimetableEntry[]> {
    return this.http.get<TeacherTimetableEntry[]>(
      `${this.baseUrl}/api/teachers/${teacherId}/timetable`,
      { params: versionParams(query) },
    );
  }

  addEntry(
    schoolClassId: number,
    request: CreateTimetableEntryRequest,
  ): Observable<TimetableEntry> {
    return this.http.post<TimetableEntry>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable`,
      request,
    );
  }

  updateEntry(
    schoolClassId: number,
    id: number,
    request: CreateTimetableEntryRequest,
  ): Observable<TimetableEntry> {
    return this.http.put<TimetableEntry>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable/${id}`,
      request,
    );
  }

  deleteEntry(schoolClassId: number, id: number): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable/${id}`,
    );
  }
}
