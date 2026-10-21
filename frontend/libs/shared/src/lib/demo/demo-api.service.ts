import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable, tap } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { AUTH_REFRESH_VIA_COOKIE } from '../auth/auth-session.config';
import { AuthApiService } from '../auth/auth-api.service';
import { LoginResponse } from '../auth/login-response.model';
import { SchoolType } from '../evidence/student-term';

/** What the "Vyzkoušet" wizard asks before generating the school. */
export interface DemoSetupRequest {
  type: SchoolType;
  name: string;
  /** Parallel classes per grade, 1–3. */
  classesPerGrade: number;
  /** Optional — the demo's link and expiry reminders go there. */
  email?: string;
  /** May Soovioo contact the visitor (kept after the demo is deleted). */
  consent?: boolean;
}

export type DemoRole = 'Admin' | 'Teacher' | 'Parent' | 'Student';

export interface DemoCreated {
  slug: string;
  expiresAt: string;
  /** Role → the user the wizard's "za koho" logs in as. */
  featuredUserIds: Record<DemoRole, number>;
}

export interface DemoUser {
  id: number;
  name: string;
  role: DemoRole;
  /** Class, children or aprobace — tells the users apart in the picker. */
  detail: string;
  /** The most to see as this one — offered first. */
  featured: boolean;
}

export interface DemoEmail {
  id: number;
  to: string;
  subject: string;
  htmlBody: string;
  createdAt: string;
}

export interface DemoFeedbackRequest {
  rating: number | null;
  text: string | null;
  email: string | null;
  consent: boolean;
  /** The role the visitor was looking through. */
  role: string | null;
}

export interface DemoContact {
  id: number;
  email: string;
  consent: boolean;
  /** "wizard" or "feedback". */
  source: string;
  demoSlug: string;
  demoExists: boolean;
  createdAt: string;
}

export interface DemoFeedback {
  id: number;
  demoSlug: string;
  rating: number | null;
  text: string | null;
  email: string | null;
  consent: boolean;
  role: string | null;
  createdAt: string;
}

/** Anonymous demo endpoints (DemoController) — the slug is the key. */
@Injectable({ providedIn: 'root' })
export class DemoApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly authApi = inject(AuthApiService);
  private readonly viaCookie = inject(AUTH_REFRESH_VIA_COOKIE);

  private url(slug?: string): string {
    return `${this.baseUrl}/api/public/demo${slug ? `/${encodeURIComponent(slug)}` : ''}`;
  }

  create(request: DemoSetupRequest): Observable<DemoCreated> {
    return this.http.post<DemoCreated>(this.url(), request);
  }

  /** Fresh data from the wizard's answers; the deletion date stays. */
  reset(slug: string, request: DemoSetupRequest): Observable<DemoCreated> {
    return this.http.post<DemoCreated>(
      `${this.url(slug)}/reset`,
      request,
      this.authApi.cookieOptions(),
    );
  }

  /** Everything but the log and one admin account; returns that admin. */
  clear(slug: string): Observable<{ adminUserId: number }> {
    return this.http.post<{ adminUserId: number }>(
      `${this.url(slug)}/clear`,
      {},
      this.authApi.cookieOptions(),
    );
  }

  delete(slug: string): Observable<void> {
    return this.http.delete<void>(this.url(slug), this.authApi.cookieOptions());
  }

  users(slug: string): Observable<DemoUser[]> {
    return this.http.get<DemoUser[]>(`${this.url(slug)}/users`);
  }

  /** Passwordless: picking the user is the login. */
  login(slug: string, userId: number): Observable<LoginResponse> {
    return this.http
      .post<LoginResponse>(
        `${this.url(slug)}/login`,
        { userId, useCookie: this.viaCookie },
        this.authApi.cookieOptions(),
      )
      .pipe(tap((response) => this.authApi.store(response)));
  }

  /** E-mails the demo "sent" (never actually sent) — newest first. */
  emails(): Observable<DemoEmail[]> {
    return this.http.get<DemoEmail[]>(`${this.baseUrl}/api/demo/emails`);
  }

  feedback(slug: string, request: DemoFeedbackRequest): Observable<void> {
    return this.http.post<void>(`${this.url(slug)}/feedback`, request);
  }

  /** SuperAdmin: visitors who left an e-mail. */
  contacts(): Observable<DemoContact[]> {
    return this.http.get<DemoContact[]>(
      `${this.baseUrl}/api/superadmin/demo/contacts`,
    );
  }

  contactsCsv(): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/api/superadmin/demo/contacts.csv`, {
      responseType: 'blob',
    });
  }

  /** SuperAdmin: what visitors thought. */
  allFeedback(): Observable<DemoFeedback[]> {
    return this.http.get<DemoFeedback[]>(
      `${this.baseUrl}/api/superadmin/demo/feedback`,
    );
  }
}
