import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { PagedResult } from '../common/paged-result.model';

export interface FeedbackRequest {
  rating: number | null;
  text: string | null;
  email: string | null;
  /** May the team write back — replies need it. */
  consent: boolean;
  /** The role the user was looking through. */
  role: string | null;
}

export interface Feedback {
  id: number;
  /** As the school was named when it was sent. */
  schoolName: string | null;
  /** Set for a demo's feedback. */
  demoSlug: string | null;
  /** False once the school (or demo) was deleted. */
  schoolExists: boolean;
  /** Who sent it from a school, while the account exists. */
  userName: string | null;
  rating: number | null;
  text: string | null;
  email: string | null;
  consent: boolean;
  role: string | null;
  createdAt: string;
  /** When a SuperAdmin first opened it; null = new. */
  seenAt: string | null;
  flagged: boolean;
  resolvedAt: string | null;
  replyCount: number;
  lastReplyAt: string | null;
}

export interface FeedbackReply {
  id: number;
  text: string;
  sentBy: string;
  sentAt: string;
}

export type FeedbackState =
  'Unseen' | 'Unanswered' | 'Answered' | 'Flagged' | 'Open' | 'Resolved';

export type FeedbackSource = 'School' | 'Demo';

export interface FeedbackQuery {
  state: FeedbackState | null;
  source: FeedbackSource | null;
  /** 1–5, or 0 for "without a rating". */
  rating: number | null;
  /** Text, e-mail, role, school or demo contains it. */
  search: string | null;
  page: number;
  pageSize: number;
}

/** Only the fields sent change. */
export interface FeedbackUpdate {
  seen?: boolean;
  flagged?: boolean;
  resolved?: boolean;
}

/** What users think of Soovioo: sent from Nastavení (the demo has its
 * own endpoint, DemoApiService.feedback), read by SuperAdmin. */
@Injectable({ providedIn: 'root' })
export class FeedbackApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  send(request: FeedbackRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/feedback`, request);
  }

  /** SuperAdmin: newest first. */
  list(query: FeedbackQuery): Observable<PagedResult<Feedback>> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query)) {
      if (value !== null && value !== '') {
        params = params.set(key, String(value));
      }
    }
    return this.http.get<PagedResult<Feedback>>(this.adminUrl(), { params });
  }

  unseenCount(): Observable<number> {
    return this.http.get<number>(`${this.adminUrl()}/unseen-count`);
  }

  update(id: number, update: FeedbackUpdate): Observable<Feedback> {
    return this.http.patch<Feedback>(`${this.adminUrl()}/${id}`, update);
  }

  /** Spam, or someone asking for it to be erased. */
  delete(id: number): Observable<void> {
    return this.http.delete<void>(`${this.adminUrl()}/${id}`);
  }

  replies(id: number): Observable<FeedbackReply[]> {
    return this.http.get<FeedbackReply[]>(`${this.adminUrl()}/${id}/replies`);
  }

  /** E-mailed to the sender; kept only once it went out. */
  reply(id: number, text: string): Observable<FeedbackReply> {
    return this.http.post<FeedbackReply>(`${this.adminUrl()}/${id}/replies`, {
      text,
    });
  }

  private adminUrl(): string {
    return `${this.baseUrl}/api/superadmin/feedback`;
  }
}
