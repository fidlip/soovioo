import { Component, inject, signal, viewChild } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Popover } from '@openng/optimus-ui/popover';
import {
  AppNotification,
  DateFormatPipe,
  NotificationType,
  NotificationsApiService,
} from '@soovioo/shared';
import { Loading } from '../loading/loading';
import { SchoolContextService } from '../school-context';
import { UnreadCountsService } from '../unread-counts.service';

const TYPE_ICONS: Record<NotificationType, string> = {
  Grade: 'pi-star',
  Message: 'pi-envelope',
  Absence: 'pi-calendar-times',
  Excuse: 'pi-check-circle',
  Homework: 'pi-book',
  Event: 'pi-calendar',
  Photo: 'pi-image',
  Timetable: 'pi-cog',
};

/** The bell's popover with the notifications — a click opens the thing
 * it's about. The buttons that open it are the app shell's (sidebar, phone
 * top bar), they call `toggle`. */
@Component({
  selector: 'app-notification-bell',
  imports: [DateFormatPipe, Button, Loading, Popover],
  templateUrl: './notification-bell.html',
})
export class NotificationBell {
  protected readonly unread = inject(UnreadCountsService);
  private readonly api = inject(NotificationsApiService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);

  private readonly popover = viewChild.required(Popover);

  protected readonly items = signal<AppNotification[]>([]);
  protected readonly hasMore = signal(false);
  protected readonly loading = signal(false);

  protected readonly icons = TYPE_ICONS;

  toggle(event: Event): void {
    this.popover().toggle(event);
  }

  /** Fresh list each time it opens. */
  protected load(): void {
    this.unread.refresh();
    this.fetch();
  }

  protected loadMore(): void {
    const items = this.items();
    this.fetch(items[items.length - 1]?.id);
  }

  private fetch(beforeId?: number): void {
    this.loading.set(true);
    this.api.getNotifications(beforeId).subscribe({
      next: (page) => {
        this.items.update((items) =>
          beforeId ? [...items, ...page.items] : page.items,
        );
        this.hasMore.set(page.hasMore);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('getNotifications failed', err);
        this.loading.set(false);
      },
    });
  }

  protected open(notification: AppNotification): void {
    if (!notification.read) {
      this.markRead(notification);
    }
    this.popover().hide();
    this.router.navigate(this.schoolContext.link(notification.page), {
      queryParams: !notification.targetId
        ? {}
        : notification.page === 'messages'
          ? { conversation: notification.targetId }
          : notification.page === 'timetable'
            ? { version: notification.targetId, generation: 'result' }
            : {},
    });
  }

  protected markAllRead(): void {
    this.api.markAllRead().subscribe({
      next: () => {
        this.items.update((items) => items.map((n) => ({ ...n, read: true })));
        this.unread.notifications.set(0);
      },
      error: (err) => console.error('markAllRead failed', err),
    });
  }

  private markRead(notification: AppNotification): void {
    this.api.markRead(notification.id).subscribe({
      next: () => {
        this.items.update((items) =>
          items.map((n) =>
            n.id === notification.id ? { ...n, read: true } : n,
          ),
        );
        this.unread.refresh();
      },
      error: (err) => console.error('markRead failed', err),
    });
  }
}
