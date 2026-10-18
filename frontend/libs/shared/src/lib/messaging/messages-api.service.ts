import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolClass } from '../evidence/school-class.model';
import {
  BroadcastRequest,
  BroadcastResult,
  Contact,
  Conversation,
  ConversationSummary,
  Message,
  MessageAttachment,
  StartConversationRequest,
} from './message.model';

/** Messaging as conversations — see ConversationsController. */
@Injectable({ providedIn: 'root' })
export class MessagesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getConversations(): Observable<ConversationSummary[]> {
    return this.http.get<ConversationSummary[]>(
      `${this.baseUrl}/api/conversations`,
    );
  }

  getUnreadCount(): Observable<number> {
    return this.http.get<number>(
      `${this.baseUrl}/api/conversations/unread-count`,
    );
  }

  getConversation(id: number): Observable<Conversation> {
    return this.http.get<Conversation>(
      `${this.baseUrl}/api/conversations/${id}`,
    );
  }

  startConversation(
    request: StartConversationRequest,
  ): Observable<Conversation> {
    return this.http.post<Conversation>(
      `${this.baseUrl}/api/conversations`,
      request,
    );
  }

  reply(conversationId: number, body: string): Observable<Message> {
    return this.http.post<Message>(
      `${this.baseUrl}/api/conversations/${conversationId}/messages`,
      { body },
    );
  }

  markRead(conversationId: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/conversations/${conversationId}/read`,
      {},
    );
  }

  /** Parent/pupil only — who they may write to about `studentId`. */
  getContacts(studentId: number): Observable<Contact[]> {
    return this.http.get<Contact[]>(
      `${this.baseUrl}/api/conversations/contacts`,
      { params: { studentId } },
    );
  }

  /** Sender only; the conversation disappears with its last message. */
  deleteMessage(conversationId: number, messageId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/conversations/${conversationId}/messages/${messageId}`,
    );
  }

  /** Separate multipart step after sending — one call per message (a
   * broadcast attaches to every fan-out copy). */
  uploadAttachment(
    conversationId: number,
    messageId: number,
    file: File,
  ): Observable<MessageAttachment> {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<MessageAttachment>(
      `${this.baseUrl}/api/conversations/${conversationId}/messages/${messageId}/attachments`,
      form,
    );
  }

  downloadAttachment(
    conversationId: number,
    messageId: number,
    attachmentId: number,
  ): Observable<Blob> {
    return this.http.get(
      `${this.baseUrl}/api/conversations/${conversationId}/messages/${messageId}/attachments/${attachmentId}`,
      { responseType: 'blob' },
    );
  }

  getBroadcastableClasses(): Observable<SchoolClass[]> {
    return this.http.get<SchoolClass[]>(
      `${this.baseUrl}/api/messages/broadcastable-classes`,
    );
  }

  broadcastToClass(
    schoolClassId: number,
    request: BroadcastRequest,
  ): Observable<BroadcastResult> {
    return this.http.post<BroadcastResult>(
      `${this.baseUrl}/api/school-classes/${schoolClassId}/messages/broadcast`,
      request,
    );
  }

  broadcastToSchool(request: BroadcastRequest): Observable<BroadcastResult> {
    return this.http.post<BroadcastResult>(
      `${this.baseUrl}/api/messages/broadcast`,
      request,
    );
  }
}
