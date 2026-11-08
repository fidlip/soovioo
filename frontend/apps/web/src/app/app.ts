import {
  Component,
  ElementRef,
  afterNextRender,
  inject,
  viewChild,
} from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterOutlet,
} from '@angular/router';
import { ConfirmDialog } from '@openng/optimus-ui/confirmdialog';
import { Toast } from '@openng/optimus-ui/toast';
import { filter, map } from 'rxjs';
import { AuthApiService } from '@soovioo/shared';
import { DarkModeService } from './dark-mode.service';
import { installPlaceholderTitles } from './placeholder-title';
import { DemoShell } from './demo/demo-shell';
import { LinkAccountDialog } from './link-account/link-account-dialog';
import { canLinkAccounts } from './link-account/can-link-accounts';
import { AppNotices } from './layout/app-notices/app-notices';
import { AppSidebar } from './layout/app-sidebar/app-sidebar';
import { startBadgeRefresh } from './layout/badge-refresh';
import { PublicHeader } from './layout/public-header/public-header';
import { SessionActionsService } from './layout/session-actions.service';
import { SidebarStateService } from './layout/sidebar-state.service';
import { SchoolContextService } from './school-context';

@Component({
  selector: 'app-root',
  imports: [
    ConfirmDialog,
    Toast,
    RouterOutlet,
    AppNotices,
    AppSidebar,
    DemoShell,
    LinkAccountDialog,
    PublicHeader,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  protected readonly sidebarState = inject(SidebarStateService);
  private readonly schoolContext = inject(SchoolContextService);

  private readonly confirmDialog = viewChild(ConfirmDialog, {
    read: ElementRef<HTMLElement>,
  });

  /** Routes opt out of the app shell (sidebar / header) via `data: { hideHeader: true }`
   * (login and accept-invite pages). Read from the deepest activated route,
   * since the flag sits on a child of the `:schoolSlug` route in path-based
   * tenant mode. */
  protected readonly hideHeader = toSignal(
    this.router.events.pipe(
      filter((e) => e instanceof NavigationEnd),
      map(() => App.deepestChild(this.router.routerState.snapshot.root)),
      map((route) => route.data['hideHeader'] === true),
    ),
    { initialValue: false },
  );

  constructor() {
    installPlaceholderTitles();
    // optimus-ui's ConfirmDialog puts a static role="alertdialog" on its
    // inner <p-dialog> host too, which stays in the DOM, unnamed, even with
    // no dialog open (an a11y violation on every page). The real dialog
    // element inside gets the role and its aria-labelledby on its own.
    afterNextRender(() =>
      this.confirmDialog()
        ?.nativeElement.querySelector('p-dialog[role]')
        ?.removeAttribute('role'),
    );
    // Created at startup only so the persisted/OS dark-mode class lands on
    // <html> on every page — the toggle itself lives on the Nastavení page,
    // not in the header.
    inject(DarkModeService);
    // Created at startup for its return to the login form once the session
    // ends while the tab is open.
    inject(SessionActionsService);
    startBadgeRefresh(this.hideHeader);
  }

  private static deepestChild(
    route: ActivatedRouteSnapshot,
  ): ActivatedRouteSnapshot {
    while (route.firstChild) route = route.firstChild;
    return route;
  }

  /** Sidebar for logged-in users everywhere except the auth pages. A plain
   * method, not a computed: `AuthApiService` isn't signal-based. */
  protected showSidebar(): boolean {
    return !this.hideHeader() && this.isLoggedIn();
  }

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  protected canLinkAccounts(): boolean {
    return canLinkAccounts(this.authApi, this.schoolContext.slug());
  }
}
