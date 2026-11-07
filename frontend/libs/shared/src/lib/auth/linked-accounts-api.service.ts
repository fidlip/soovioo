import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { AuthApiService } from './auth-api.service';

/** One of the user's accounts at another school. */
export interface LinkedAccount {
  userId: number;
  schoolName: string;
  username: string;
  /** Unread notifications under that account's bell. */
  unread: number;
}

export interface LinkAccountRequest {
  schoolSlug: string;
  username: string;
  password: string;
}

/** A school to link with. */
export interface LinkableSchool {
  name: string;
  schoolSlug: string;
}

/** A request for linking accounts, on its way between the two schools. */
export interface LinkRequest {
  code: string;
  /** The school whose page goes on with the code. */
  schoolSlug: string;
}

/** The account asking to be linked. */
export interface LinkRequestPreview {
  username: string;
  schoolName: string;
  schoolSlug: string;
}

export interface AccountSwitch {
  code: string;
  schoolSlug: string;
}

/** The list itself comes with the unread counts, see `UnreadCounts`. */
@Injectable({ providedIn: 'root' })
export class LinkedAccountsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);
  private readonly authApi = inject(AuthApiService);

  searchSchools(search: string): Observable<LinkableSchool[]> {
    return this.http.get<LinkableSchool[]>(
      `${this.baseUrl}/api/linked-accounts/schools`,
      { params: { search } },
    );
  }

  /** Web: linking goes through the other school's own login. */
  startRequest(schoolSlug: string): Observable<LinkRequest> {
    return this.http.post<LinkRequest>(
      `${this.baseUrl}/api/linked-accounts/requests`,
      { schoolSlug },
    );
  }

  previewRequest(code: string): Observable<LinkRequestPreview> {
    return this.http.post<LinkRequestPreview>(
      `${this.baseUrl}/api/linked-accounts/requests/preview`,
      { code },
    );
  }

  approveRequest(code: string): Observable<LinkRequest> {
    return this.http.post<LinkRequest>(
      `${this.baseUrl}/api/linked-accounts/requests/approve`,
      { code },
    );
  }

  completeRequest(code: string): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/linked-accounts/requests/complete`,
      { code },
    );
  }

  /** Mobile: the app's own form logs into the other account. */
  link(request: LinkAccountRequest): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/linked-accounts`, request);
  }

  unlink(userId: number): Observable<void> {
    return this.http.delete<void>(
      `${this.baseUrl}/api/linked-accounts/${userId}`,
    );
  }

  /** A one-time code for `AuthApiService.redeemLoginCode`. Sends the
   * cookie, so the other account's session lasts as long as this one's. */
  switchTo(userId: number): Observable<AccountSwitch> {
    return this.http.post<AccountSwitch>(
      `${this.baseUrl}/api/linked-accounts/${userId}/switch`,
      null,
      this.authApi.cookieOptions(),
    );
  }
}
