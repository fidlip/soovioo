import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';

export interface FeedbackRequest {
  rating: number | null;
  text: string | null;
  email: string | null;
  /** May the team write back — replies need it. */
  consent: boolean;
  /** The role the user was looking through. */
  role: string | null;
}

export interface FeedbackAvailability {
  /** The instance sends feedback to the Soovioo team (it's connected to the backoffice). */
  available: boolean;
}

/** What users think of Soovioo: sent from Nastavení to the Soovioo team (the
 * demo has its own endpoint, DemoApiService.feedback). */
@Injectable({ providedIn: 'root' })
export class FeedbackApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  availability(): Observable<FeedbackAvailability> {
    return this.http.get<FeedbackAvailability>(`${this.baseUrl}/api/feedback`);
  }

  send(request: FeedbackRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/feedback`, request);
  }
}
