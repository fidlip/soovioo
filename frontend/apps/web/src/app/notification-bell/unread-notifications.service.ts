import { Injectable, inject, signal } from '@angular/core';
import { NotificationsApiService } from '@soovioo/shared';

/** Unread notification count for the bell — refreshed on every navigation
 * and once a minute (app.ts), and whenever the bell reads something. */
@Injectable({ providedIn: 'root' })
export class UnreadNotificationsService {
  private readonly api = inject(NotificationsApiService);

  readonly count = signal(0);

  refresh(): void {
    this.api.getUnreadCount().subscribe({
      next: (count) => this.count.set(count),
      error: () => this.count.set(0),
    });
  }
}
