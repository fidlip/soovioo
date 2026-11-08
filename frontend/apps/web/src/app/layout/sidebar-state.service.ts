import {
  DestroyRef,
  Injectable,
  computed,
  inject,
  signal,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

const SIDEBAR_COLLAPSED_STORAGE_KEY = 'soovioo-sidebar-collapsed';

/** md..lg in Tailwind terms — the sidebar starts collapsed to icons here. */
const TABLET_QUERY = '(min-width: 768px) and (max-width: 1023.98px)';

/** Whether the sidebar is collapsed to icons or open as the mobile drawer —
 * the sidebar and the page beside it (its margin) both follow it. */
@Injectable({ providedIn: 'root' })
export class SidebarStateService {
  /** Mobile (< md) drawer. Below md the sidebar is off-canvas and the CSS
   * ignores `collapsed`, so the drawer always shows labels. */
  readonly drawerOpen = signal(false);

  private readonly isTablet = signal(
    window.matchMedia?.(TABLET_QUERY).matches ?? false,
  );

  /** Desktop choice, persisted. Tablet keeps its own session-only state so
   * expanding the sidebar on a narrow window doesn't flip the desktop
   * default. */
  private readonly desktopCollapsed = signal(
    SidebarStateService.readStoredCollapsed(),
  );
  private readonly tabletCollapsed = signal(true);

  readonly collapsed = computed(() =>
    this.isTablet() ? this.tabletCollapsed() : this.desktopCollapsed(),
  );

  constructor() {
    const tabletQuery = window.matchMedia?.(TABLET_QUERY);
    if (tabletQuery) {
      const onChange = (e: MediaQueryListEvent) => this.isTablet.set(e.matches);
      tabletQuery.addEventListener('change', onChange);
      inject(DestroyRef).onDestroy(() =>
        tabletQuery.removeEventListener('change', onChange),
      );
    }

    // The mobile drawer covers the page, so close it once a link in it has
    // done its job.
    inject(Router)
      .events.pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.drawerOpen.set(false));
  }

  toggleCollapsed(): void {
    if (this.isTablet()) {
      this.tabletCollapsed.update((c) => !c);
      return;
    }
    const next = !this.desktopCollapsed();
    this.desktopCollapsed.set(next);
    try {
      localStorage.setItem(SIDEBAR_COLLAPSED_STORAGE_KEY, String(next));
    } catch {
      // Blocked storage — the choice just won't survive a reload.
    }
  }

  private static readStoredCollapsed(): boolean {
    try {
      return localStorage.getItem(SIDEBAR_COLLAPSED_STORAGE_KEY) === 'true';
    } catch {
      return false;
    }
  }
}
