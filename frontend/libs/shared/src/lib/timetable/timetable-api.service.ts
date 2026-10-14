import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CreateTimetableEntryRequest, TimetableEntry } from './timetable-entry.model';

@Injectable({ providedIn: 'root' })
export class TimetableApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getTimetable(schoolClassId: number): Observable<TimetableEntry[]> {
    return this.http.get<TimetableEntry[]>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/timetable`,
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
}
