import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateTimetableEntryRequest,
  TeacherTimetableEntry,
  TimetableEntry,
} from './timetable-entry.model';

@Injectable({ providedIn: 'root' })
export class TimetableApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getTimetable(schoolClassId: number): Observable<TimetableEntry[]> {
    return this.http.get<TimetableEntry[]>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable`,
    );
  }

  /** A teacher's own schedule across every class they teach — unlike
   * getTimetable, not scoped to one class. */
  getTeacherTimetable(teacherId: number): Observable<TeacherTimetableEntry[]> {
    return this.http.get<TeacherTimetableEntry[]>(
      `${this.baseUrl}/api/teachers/${teacherId}/timetable`,
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
