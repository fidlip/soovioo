import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  NotificationPage,
  NotificationSettings,
  NotificationType,
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

  getUnreadCount(): Observable<number> {
    return this.http.get<number>(
      `${this.baseUrl}/api/notifications/unread-count`,
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
}
