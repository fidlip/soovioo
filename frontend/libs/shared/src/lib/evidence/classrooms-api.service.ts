import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Classroom, CreateClassroomRequest } from './classroom.model';

@Injectable({ providedIn: 'root' })
export class ClassroomsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getClassrooms(): Observable<Classroom[]> {
    return this.http.get<Classroom[]>(`${this.baseUrl}/api/classrooms`);
  }

  addClassroom(request: CreateClassroomRequest): Observable<Classroom> {
    return this.http.post<Classroom>(`${this.baseUrl}/api/classrooms`, request);
  }

  updateClassroom(
    id: number,
    request: CreateClassroomRequest,
  ): Observable<Classroom> {
    return this.http.put<Classroom>(
      `${this.baseUrl}/api/classrooms/${id}`,
      request,
    );
  }

  /** Rejected (400) while any timetable entry or subject default references it. */
  deleteClassroom(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/classrooms/${id}`);
  }
}
