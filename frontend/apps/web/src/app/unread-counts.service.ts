import { Injectable, inject, signal } from '@angular/core';
import { NotificationsApiService } from '@soovioo/shared';

/** Unread numbers for the bell and the sidebar "Zprávy" badge, in one
 * request — refreshed on every navigation and once a minute (app.ts), and
 * whenever something gets read. */
@Injectable({ providedIn: 'root' })
export class UnreadCountsService {
  private readonly api = inject(NotificationsApiService);

  readonly notifications = signal(0);
  readonly messages = signal(0);

  refresh(): void {
    this.api.getUnreadCounts().subscribe({
      next: (counts) => {
        this.notifications.set(counts.notifications);
        this.messages.set(counts.messages);
      },
      // A failed badge isn't worth a console error on every navigation.
      error: () => {
        this.notifications.set(0);
        this.messages.set(0);
      },
    });
  }
}
