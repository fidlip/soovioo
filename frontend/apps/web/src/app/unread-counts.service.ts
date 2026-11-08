import { Injectable, inject, signal } from '@angular/core';
import {
  FeedbackApiService,
  LinkedAccount,
  NotificationsApiService,
} from '@soovioo/shared';

/** Unread numbers for the bell and the sidebar "Zprávy" badge, in one
 * request — refreshed on every navigation and once a minute
 * (`startBadgeRefresh`), and whenever something gets read. A SuperAdmin's
 * "Zpětná vazba" badge the same way. */
@Injectable({ providedIn: 'root' })
export class UnreadCountsService {
  private readonly api = inject(NotificationsApiService);
  private readonly feedbackApi = inject(FeedbackApiService);

  readonly notifications = signal(0);
  readonly messages = signal(0);
  /** Stays the same array while nothing changes — menu items are built from
   * it, see `SidebarAccountMenu.accountMenuItems`. */
  readonly linkedAccounts = signal<LinkedAccount[]>([], {
    equal: (a, b) => JSON.stringify(a) === JSON.stringify(b),
  });
  /** SuperAdmin: feedback no one opened yet. */
  readonly feedback = signal(0);

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

  refreshFeedback(): void {
    this.feedbackApi.unseenCount().subscribe({
      next: (count) => this.feedback.set(count),
      error: () => this.feedback.set(0),
    });
  }
}
