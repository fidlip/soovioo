import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  NotificationPage,
  NotificationSettings,
  NotificationType,
  SchoolNotificationSettings,
  UnreadCounts,
  UnsubscribeResult,
} from './notification.model';

/** The caller's notifications — see NotificationsController. */
@Injectable({ providedIn: 'root' })
export class NotificationsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Newest first; `beforeId` continues below the last one shown. */
  getNotifications(beforeId?: number): Observable<NotificationPage> {
    return this.http.get<NotificationPage>(
      `${this.baseUrl}/api/notifications`,
      beforeId ? { params: { beforeId } } : {},
    );
  }

  /** The bell's and the Zprávy badge's numbers in one request. */
  getUnreadCounts(): Observable<UnreadCounts> {
    return this.http.get<UnreadCounts>(
      `${this.baseUrl}/api/notifications/unread-counts`,
    );
  }

  markRead(id: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/notifications/${id}/read`,
      {},
    );
  }

  markAllRead(): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/notifications/read-all`,
      {},
    );
  }

  getSettings(): Observable<NotificationSettings> {
    return this.http.get<NotificationSettings>(
      `${this.baseUrl}/api/notifications/settings`,
    );
  }

  setSettings(muted: NotificationType[]): Observable<NotificationSettings> {
    return this.http.put<NotificationSettings>(
      `${this.baseUrl}/api/notifications/settings`,
      { muted },
    );
  }

  /** The kinds in the daily e-mail and its time (`HH:mm`, null = the school's). */
  setEmailSettings(
    muted: NotificationType[],
    digestTime: string | null,
  ): Observable<NotificationSettings> {
    return this.http.put<NotificationSettings>(
      `${this.baseUrl}/api/notifications/email-settings`,
      { muted, digestTime },
    );
  }

  /** The e-mail's "odhlásit" link — no login needed. */
  unsubscribe(token: string): Observable<UnsubscribeResult> {
    return this.http.post<UnsubscribeResult>(
      `${this.baseUrl}/api/notifications/unsubscribe`,
      { token },
    );
  }

  getSchoolSettings(): Observable<SchoolNotificationSettings> {
    return this.http.get<SchoolNotificationSettings>(
      `${this.baseUrl}/api/my-school/notifications`,
    );
  }

  setSchoolSettings(
    settings: SchoolNotificationSettings,
  ): Observable<SchoolNotificationSettings> {
    return this.http.put<SchoolNotificationSettings>(
      `${this.baseUrl}/api/my-school/notifications`,
      settings,
    );
  }
}
