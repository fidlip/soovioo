import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  RouterExtensions,
} from '@nativescript/angular';
import {
  AppNotification,
  DateFormatPipe,
  NotificationsApiService,
} from '@soovioo/shared';
import { MOBILE_PAGES } from '../../push.service';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

/** The notifications (the web's bell): tap reads it and opens its page
 * when the app has one. */
@Component({
  selector: 'ns-notifications',
  templateUrl: './notifications.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent, DateFormatPipe],
  schemas: [NO_ERRORS_SCHEMA],
})
export class NotificationsComponent {
  private readonly api = inject(NotificationsApiService);
  private readonly router = inject(RouterExtensions);

  protected readonly items = signal<AppNotification[]>([]);
  protected readonly hasMore = signal(false);

  constructor() {
    this.load();
  }

  protected load(beforeId?: number): void {
    this.api.getNotifications(beforeId).subscribe({
      next: (page) => {
        this.items.update((items) =>
          beforeId ? [...items, ...page.items] : page.items,
        );
        this.hasMore.set(page.hasMore);
      },
      error: (err) => console.error('getNotifications failed', err),
    });
  }

  protected loadMore(): void {
    const items = this.items();
    this.load(items[items.length - 1]?.id);
  }

  protected open(index: number): void {
    const notification = this.items()[index];
    if (!notification) {
      return;
    }
    if (!notification.read) {
      this.api.markRead(notification.id).subscribe({
        next: () =>
          this.items.update((items) =>
            items.map((n) =>
              n.id === notification.id ? { ...n, read: true } : n,
            ),
          ),
        error: (err) => console.error('markRead failed', err),
      });
    }
    if (MOBILE_PAGES.includes(notification.page)) {
      this.router.navigate([`/${notification.page}`]);
    }
  }

  protected markAllRead(): void {
    this.api.markAllRead().subscribe({
      next: () =>
        this.items.update((items) => items.map((n) => ({ ...n, read: true }))),
      error: (err) => console.error('markAllRead failed', err),
    });
  }
}
