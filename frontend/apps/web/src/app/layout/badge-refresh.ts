import { Signal, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';
import { filter, interval, merge } from 'rxjs';
import { LiveService } from '../live.service';
import { UnreadCountsService } from '../unread-counts.service';

/** Sidebar "Zprávy" badge and the bell (a SuperAdmin's "Zpětná vazba"):
 * whenever the live connection says they changed — and while it isn't
 * connected, on every navigation and once a minute while someone stays on
 * one page. Call from a constructor. */
export function startBadgeRefresh(hideHeader: Signal<boolean>): void {
  const authApi = inject(AuthApiService);
  const unread = inject(UnreadCountsService);
  const live = inject(LiveService);
  const offline = merge(
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd)),
    interval(60_000).pipe(filter(() => document.visibilityState === 'visible')),
  ).pipe(filter(() => !live.connected() && !hideHeader()));

  merge(live.counts$, offline)
    .pipe(
      filter(() => authApi.isLoggedIn()),
      takeUntilDestroyed(),
    )
    .subscribe(() => unread.refresh());
  merge(live.feedback$, offline)
    .pipe(
      filter(() => authApi.isLoggedIn() && authApi.isSuperAdmin()),
      takeUntilDestroyed(),
    )
    .subscribe(() => unread.refreshFeedback());
}
