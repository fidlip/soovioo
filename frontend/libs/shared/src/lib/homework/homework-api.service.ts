import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  CreateHomeworkRequest,
  Homework,
  HomeworkAttachment,
} from './homework.model';

@Injectable({ providedIn: 'root' })
export class HomeworkApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Unscoped, like GradesApiService.getGrades — Teacher/Admin get every
   * homework item in the school, Parent/Student only what's assigned to
   * their own accessible student(s). */
  getHomework(): Observable<Homework[]> {
    return this.http.get<Homework[]>(`${this.baseUrl}/api/homework`);
  }

  addHomework(request: CreateHomeworkRequest): Observable<Homework> {
    return this.http.post<Homework>(`${this.baseUrl}/api/homework`, request);
  }

  deleteHomework(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/homework/${id}`);
  }

  markComplete(id: number, studentId: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/homework/${id}/complete`, {
      studentId,
    });
  }

  /** A separate call after addHomework rather than bundled into it — same
   * reasoning as MessagesApiService.uploadAttachment. */
  uploadAttachment(id: number, file: File): Observable<HomeworkAttachment> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<HomeworkAttachment>(
      `${this.baseUrl}/api/homework/${id}/attachments`,
      formData,
    );
  }

  downloadAttachment(id: number, attachmentId: number): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/api/homework/${id}/attachments/${attachmentId}`,
      {
        responseType: 'blob',
      },
    );
  }
}
