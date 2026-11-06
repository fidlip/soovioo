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
