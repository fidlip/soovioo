import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toSignal } from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { ButtonDirective } from '@openng/optimus-ui/button';
import { Menu } from '@openng/optimus-ui/menu';
import { ConfirmDialog } from '@openng/optimus-ui/confirmdialog';
import { Toast } from '@openng/optimus-ui/toast';
import { MenuItem } from '@openng/optimus-ui/api';
import { filter, map } from 'rxjs';
import {
  AuthApiService,
  ROLE_LABELS,
  SupportApiService,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon, AppIconName } from './app-icon';
import { DarkModeService } from './dark-mode.service';
import { installPlaceholderTitles } from './placeholder-title';
import { SchoolContextService } from './school-context';
import { UnreadMessagesService } from './unread-messages.service';

const SIDEBAR_COLLAPSED_STORAGE_KEY = 'soovioo-sidebar-collapsed';

/** md..lg in Tailwind terms — the sidebar starts collapsed to icons here. */
const TABLET_QUERY = '(min-width: 768px) and (max-width: 1023.98px)';

interface NavItem {
  label: string;
  icon: AppIconName;
  link: string[];
  /** Administrace — shown to admins and teachers only. */
  staffOnly?: boolean;
}

@Component({
  selector: 'app-root',
  imports: [
    ConfirmDialog,
    Toast,
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    ButtonDirective,
    Menu,
    AppIcon,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: { '(document:keydown.escape)': 'drawerOpen.set(false)' },
})
export class App {
  protected readonly authApi = inject(AuthApiService);
  private readonly supportApi = inject(SupportApiService);
  private readonly router = inject(Router);
  protected readonly schoolContext = inject(SchoolContextService);
  protected readonly unreadMessages = inject(UnreadMessagesService);

  constructor() {
    installPlaceholderTitles();
    // Created at startup only so the persisted/OS dark-mode class lands on
    // <html> on every page — the toggle itself lives on the Nastavení page,
    // not in the header.
    inject(DarkModeService);

    const tabletQuery = window.matchMedia?.(TABLET_QUERY);
    if (tabletQuery) {
      const onChange = (e: MediaQueryListEvent) => this.isTablet.set(e.matches);
      tabletQuery.addEventListener('change', onChange);
      inject(DestroyRef).onDestroy(() =>
        tabletQuery.removeEventListener('change', onChange),
      );
    }

    // The refresh cookie expired or was revoked (logout elsewhere, password
    // change) while this tab was open — back to the login form.
    this.authApi.sessionExpired$
      .pipe(takeUntilDestroyed())
      .subscribe(() =>
        this.router.navigate(
          this.router.url.startsWith('/superadmin')
            ? ['/superadmin/login']
            : this.schoolContext.link('login'),
        ),
      );

    // The mobile drawer covers the page, so close it once a link in it has
    // done its job.
    this.router.events
      .pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => {
        this.drawerOpen.set(false);
        // Sidebar "Zprávy" badge — cheap enough to re-check per navigation.
        if (
          this.authApi.isLoggedIn() &&
          !this.authApi.isSuperAdmin() &&
          !this.hideHeader()
        ) {
          this.unreadMessages.refresh();
        }
      });
  }

  protected title = 'Soovioo';

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

  /** Mobile (< md) drawer. Below md the sidebar is off-canvas and the CSS
   * ignores `collapsed`, so the drawer always shows labels. */
  protected readonly drawerOpen = signal(false);

  private readonly isTablet = signal(
    window.matchMedia?.(TABLET_QUERY).matches ?? false,
  );

  /** Desktop choice, persisted. Tablet keeps its own session-only state so
   * expanding the sidebar on a narrow window doesn't flip the desktop
   * default. */
  private readonly desktopCollapsed = signal(App.readStoredCollapsed());
  private readonly tabletCollapsed = signal(true);

  protected readonly collapsed = computed(() =>
    this.isTablet() ? this.tabletCollapsed() : this.desktopCollapsed(),
  );

  protected toggleCollapsed(): void {
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

  /** School-agenda links. Computed (stable object references for `@for`)
   * and therefore auth-independent — the template filters `staffOnly`
   * items itself, since `AuthApiService` checks aren't signals and a
   * computed would never see a login/logout. */
  protected readonly navItems = computed<NavItem[]>(() => {
    const link = (path: string) => this.schoolContext.link(path);
    return [
      { label: 'Domů', icon: 'home', link: link('home') },
      { label: 'Známky', icon: 'grades', link: link('grades') },
      { label: 'Docházka', icon: 'absences', link: link('absences') },
      { label: 'Rozvrh', icon: 'timetable', link: link('timetable') },
      { label: 'Kalendář', icon: 'events', link: link('events') },
      { label: 'Zprávy', icon: 'messages', link: link('messages') },
      { label: 'Domácí úkoly', icon: 'homework', link: link('homework') },
      { label: 'Chování', icon: 'behavior', link: link('behavior') },
      { label: 'Platby', icon: 'payments', link: link('payments') },
      {
        label: 'Administrace',
        icon: 'admin',
        link: link('admin'),
        staffOnly: true,
      },
    ];
  });

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  /** Ends support mode: closes the audited session, then the SuperAdmin's
   * own session comes back from the refresh cookie. */
  protected leaveSupport(): void {
    const backToSuperAdmin = () =>
      this.authApi
        .restoreSession()
        .then(() => this.router.navigate(['/superadmin']));
    this.supportApi.endCurrentSession().subscribe({
      next: backToSuperAdmin,
      error: backToSuperAdmin,
    });
  }

  protected logout(): void {
    if (this.authApi.getSupportInfo()) {
      // Logging out here would end the SuperAdmin's own session too.
      this.leaveSupport();
      return;
    }
    const wasSuperAdmin = this.authApi.isSuperAdmin();
    this.authApi.logout();
    this.router.navigate(
      wasSuperAdmin ? ['/superadmin/login'] : this.schoolContext.link('login'),
    );
  }

  protected isAdmin(): boolean {
    return this.authApi.isAdmin();
  }

  /** Shows the Administrace nav link to any Teacher, not just class
   * teachers — AdminPage itself resolves whether they're actually a class
   * teacher of something (restricted view) or not (same "jen pro ... "
   * denial as before), since that needs an async lookup this synchronous
   * nav check can't do. */
  protected isTeacher(): boolean {
    return this.authApi.isTeacher();
  }

  protected isSuperAdmin(): boolean {
    return this.authApi.isSuperAdmin();
  }

  /** Accounts with several roles (e.g. a teacher who is also a parent) get
   * a "Jako: …" switcher in the sidebar. */
  protected hasSeveralRoles(): boolean {
    return this.authApi.getRoles().length > 1;
  }

  protected activeRoleLabel(): string {
    return this.roleLabel(this.authApi.getRole() ?? '');
  }

  private roleLabel(role: string): string {
    return localizeStudentTerm(
      ROLE_LABELS[role] ?? role,
      this.authApi.getSchoolType(),
    );
  }

  /** Same stable-reference requirement as userMenuItems below, but the
   * roles come from the token (not a signal) — cached per roles+active. */
  private roleMenuCache: { key: string; items: MenuItem[] } | null = null;

  protected roleMenuItems(): MenuItem[] {
    const roles = this.authApi.getRoles();
    const active = this.authApi.getRole();
    const key = `${roles.join(',')}|${active}`;
    if (this.roleMenuCache?.key !== key) {
      this.roleMenuCache = {
        key,
        items: roles.map((role) => ({
          label: this.roleLabel(role),
          icon: role === active ? 'pi pi-check' : 'pi pi-fw',
          command: () => this.switchRole(role),
        })),
      };
    }
    return this.roleMenuCache.items;
  }

  /** Every page decides what to show by the role once, when it's created —
   * a full reload onto Home is the simplest way to make all of them
   * re-decide. */
  private switchRole(role: string): void {
    if (role === this.authApi.getRole()) {
      return;
    }
    this.authApi.switchRole(role).subscribe({
      next: () =>
        window.location.assign(
          this.router.serializeUrl(
            this.router.createUrlTree(this.schoolContext.link('home')),
          ),
        ),
      error: (err) => console.error('switchRole failed', err),
    });
  }

  protected username(): string | undefined {
    return this.authApi.getUsername() ?? undefined;
  }

  /** Must be `computed()`s, not plain methods called from the template:
   * p-menu's `@for` tracks items by object reference, so a method re-run on
   * every change-detection cycle handed it a brand-new array/objects each
   * time, tearing the `<li>`/`<a>` elements down and rebuilding them
   * constantly. If that teardown landed between a real mouse's mousedown and
   * mouseup — never between a test's synthetic click() — the browser dropped
   * the click, which is why the first click on a menu item did nothing and
   * only the second one (hitting a settled DOM) worked.
   *
   * Two of them, picked in the template, rather than one branching on
   * `isSuperAdmin()`: that check isn't a signal, so a single computed would
   * stay stuck on whichever role it first saw. SuperAdmin lives outside the
   * `:schoolSlug` route tree (see app.routes.ts), so its Settings link can't
   * go through `SchoolContextService.link()`. */
  protected readonly userMenuItems = computed<MenuItem[]>(() =>
    this.buildUserMenu(this.schoolContext.link('settings')),
  );

  protected readonly superAdminUserMenuItems = computed<MenuItem[]>(() =>
    this.buildUserMenu(['/superadmin/settings']),
  );

  private buildUserMenu(settingsLink: string[]): MenuItem[] {
    return [
      { label: 'Nastavení', icon: 'pi pi-cog', routerLink: settingsLink },
      { separator: true },
      {
        label: 'Odhlásit se',
        icon: 'pi pi-sign-out',
        command: () => this.logout(),
      },
    ];
  }
}
