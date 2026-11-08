import { Component, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
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
  TimetableChange: 'pi-table',
  Substitution: 'pi-user-edit',
};

const UNREAD_ONLY_STORAGE_KEY = 'soovioo-notifications-unread-only';

function readUnreadOnly(): boolean {
  try {
    return localStorage.getItem(UNREAD_ONLY_STORAGE_KEY) === 'true';
  } catch {
    return false;
  }
}

/** The bell's popover with the notifications — a click opens the thing
 * it's about. The buttons that open it are the app shell's (sidebar, phone
 * top bar), they call `toggle`. */
@Component({
  selector: 'app-notification-bell',
  imports: [DateFormatPipe, Button, Checkbox, FormsModule, Loading, Popover],
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
  protected readonly unreadOnly = signal(readUnreadOnly());

  protected readonly icons = TYPE_ICONS;

  toggle(event: Event): void {
    this.popover().toggle(event);
  }

  /** Fresh list each time it opens. */
  protected load(): void {
    this.unread.refresh();
    this.fetch();
  }

  protected setUnreadOnly(unreadOnly: boolean): void {
    this.unreadOnly.set(unreadOnly);
    try {
      localStorage.setItem(UNREAD_ONLY_STORAGE_KEY, String(unreadOnly));
    } catch {
      // Blocked storage — the filter still works until a reload.
    }
    this.items.set([]);
    this.hasMore.set(false);
    this.fetch();
  }

  protected loadMore(): void {
    const items = this.items();
    this.fetch(items[items.length - 1]?.id);
  }

  private fetch(beforeId?: number): void {
    this.loading.set(true);
    this.api.getNotifications(beforeId, this.unreadOnly()).subscribe({
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
    const [page, query = ''] = notification.link.split('?');
    this.router.navigate(this.schoolContext.link(page), {
      queryParams: Object.fromEntries(new URLSearchParams(query)),
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
