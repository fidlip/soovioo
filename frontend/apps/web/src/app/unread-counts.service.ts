import { Injectable, inject, signal } from '@angular/core';
import { LinkedAccount, NotificationsApiService } from '@soovioo/shared';

/** Unread numbers for the bell and the sidebar "Zprávy" badge, in one
 * request — refreshed on every navigation and once a minute (app.ts), and
 * whenever something gets read. */
@Injectable({ providedIn: 'root' })
export class UnreadCountsService {
  private readonly api = inject(NotificationsApiService);

  readonly notifications = signal(0);
  readonly messages = signal(0);
  /** Stays the same array while nothing changes — menu items are built from
   * it, see `App.roleMenuItems`. */
  readonly linkedAccounts = signal<LinkedAccount[]>([], {
    equal: (a, b) => JSON.stringify(a) === JSON.stringify(b),
  });

  refresh(): void {
    this.api.getUnreadCounts().subscribe({
      next: (counts) => {
        this.notifications.set(counts.notifications);
        this.messages.set(counts.messages);
        this.linkedAccounts.set(counts.linkedAccounts ?? []);
      },
      // A failed badge isn't worth a console error on every navigation.
      error: () => {
        this.notifications.set(0);
        this.messages.set(0);
        this.linkedAccounts.set([]);
      },
    });
  }
}
