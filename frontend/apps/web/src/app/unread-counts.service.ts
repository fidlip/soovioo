import { Injectable, inject, signal } from '@angular/core';
import {
  FeedbackApiService,
  LinkedAccount,
  NotificationsApiService,
  SupportPresence,
} from '@soovioo/shared';

/** Unread numbers for the bell and the sidebar "Zprávy" badge (and the
 * support-mode banners), in one request — refreshed when the live
 * connection says they changed (`startBadgeRefresh`), and whenever
 * something gets read. A SuperAdmin's "Zpětná vazba" badge the same way. */
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
  /** SuperAdmins in the school as the user or for their ticket — the
   * banners (`AppNotices`), as fresh as the badges. */
  readonly support = signal<SupportPresence[]>([], {
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
        this.support.set(counts.support ?? []);
      },
      // A failed badge isn't worth a console error on every navigation.
      error: () => {
        this.notifications.set(0);
        this.messages.set(0);
        this.linkedAccounts.set([]);
        this.support.set([]);
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
