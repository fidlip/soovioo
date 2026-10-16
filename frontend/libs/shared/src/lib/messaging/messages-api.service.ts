import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Message, SendMessageRequest } from './message.model';

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
}
