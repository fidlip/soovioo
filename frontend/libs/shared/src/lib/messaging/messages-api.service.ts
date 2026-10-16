import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolClass } from '../evidence/school-class.model';
import { BroadcastResult, Message, SendMessageRequest } from './message.model';

@Injectable({ providedIn: 'root' })
export class MessagesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getMessages(studentId: number): Observable<Message[]> {
    return this.http.get<Message[]>(`${this.baseUrl}/api/students/${studentId}/messages`);
  }

  sendMessage(studentId: number, request: SendMessageRequest): Observable<Message> {
    return this.http.post<Message>(
      `${this.baseUrl}/api/students/${studentId}/messages`,
      request,
    );
  }

  markRead(studentId: number, messageId: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/students/${studentId}/messages/${messageId}/read`,
      {},
    );
  }

  deleteMessage(studentId: number, messageId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/students/${studentId}/messages/${messageId}`,
    );
  }

  /** Classes the caller may broadcast to — every class for Admin, only ones
   * they teach (class teacher or has a timetable entry) for Teacher. */
  getBroadcastableClasses(): Observable<SchoolClass[]> {
    return this.http.get<SchoolClass[]>(`${this.baseUrl}/api/messages/broadcastable-classes`);
  }

  /** Sends one copy of the message into every student's thread in the class.
   * Rejected (403) for a Teacher who doesn't teach that class. */
  broadcastToClass(schoolClassId: number, request: SendMessageRequest): Observable<BroadcastResult> {
    return this.http.post<BroadcastResult>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/messages/broadcast`,
      request,
    );
  }

  /** Sends one copy of the message into every student's thread in the
   * school. Admin only. */
  broadcastToSchool(request: SendMessageRequest): Observable<BroadcastResult> {
    return this.http.post<BroadcastResult>(`${this.baseUrl}/api/messages/broadcast`, request);
  }
}
