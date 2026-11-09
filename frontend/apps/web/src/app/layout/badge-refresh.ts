import { Signal, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';
import { filter, interval, merge } from 'rxjs';
import { UnreadCountsService } from '../unread-counts.service';

/** Sidebar "Zprávy" badge and the bell (a SuperAdmin's "Zpětná vazba"):
 * re-checked on every navigation, cheap enough, and once a minute while
 * someone stays on one page. Call from a constructor. */
export function startBadgeRefresh(hideHeader: Signal<boolean>): void {
  const authApi = inject(AuthApiService);
  const unread = inject(UnreadCountsService);
  merge(
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd)),
    interval(60_000).pipe(filter(() => document.visibilityState === 'visible')),
  )
    .pipe(takeUntilDestroyed())
    .subscribe(() => {
      if (!authApi.isLoggedIn() || hideHeader()) {
        return;
      }
      if (authApi.isSuperAdmin()) {
        unread.refreshFeedback();
      }
      unread.refresh();
    });
}
