import {
  Component,
  DestroyRef,
  ElementRef,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import {
  takeUntilDestroyed,
  toObservable,
  toSignal,
} from '@angular/core/rxjs-interop';
import {
  ActivatedRouteSnapshot,
  NavigationEnd,
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { canSeeTeachingArea } from './teaching/teaching-area-access';
import { ButtonDirective } from '@openng/optimus-ui/button';
import { Menu } from '@openng/optimus-ui/menu';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import { ConfirmDialog } from '@openng/optimus-ui/confirmdialog';
import { Toast } from '@openng/optimus-ui/toast';
import { MenuItem } from '@openng/optimus-ui/api';
import { catchError, filter, interval, map, of, switchMap } from 'rxjs';
import {
  AuthApiService,
  LinkedAccount,
  LinkedAccountsApiService,
  RoleRef,
  SupportApiService,
} from '@soovioo/shared';
import { AppIcon, AppIconName } from './app-icon';
import { DarkModeService } from './dark-mode.service';
import { NotificationBell } from './notification-bell/notification-bell';
import { installPlaceholderTitles } from './placeholder-title';
import { DemoBar } from './demo/demo-bar';
import { DemoEmailPreview } from './demo/demo-email-preview';
import { DemoFeedbackDialog } from './demo/demo-feedback-dialog';
import { DemoFeedbackService } from './demo/demo-feedback.service';
import { SchoolLookupService } from './school-lookup.service';
import {
  SchoolContextService,
  isDemoSlug,
  schoolUrl,
  superAdminUrl,
} from './school-context';
import { LinkAccountDialog } from './link-account/link-account-dialog';
import { LinkAccountDialogService } from './link-account/link-account-dialog.service';
import { SessionSyncService } from './session-sync.service';
import { UnreadCountsService } from './unread-counts.service';

const SIDEBAR_COLLAPSED_STORAGE_KEY = 'soovioo-sidebar-collapsed';

/** md..lg in Tailwind terms — the sidebar starts collapsed to icons here. */
const TABLET_QUERY = '(min-width: 768px) and (max-width: 1023.98px)';

interface NavItem {
  label: string;
  icon: AppIconName;
  link: string[];
  /** Administrace — the whole of it, or a class teacher's pupils. */
  staffOnly?: boolean;
  /** Logy. */
  adminOnly?: boolean;
  /** Lidé — parents and pupils only (staff have Administrace). */
  familyOnly?: boolean;
  /** Mé děti — parents only. */
  parentOnly?: boolean;
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
    Tooltip,
    AppIcon,
    DemoBar,
    DemoEmailPreview,
    DemoFeedbackDialog,
    LinkAccountDialog,
    NotificationBell,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  host: { '(document:keydown.escape)': 'drawerOpen.set(false)' },
})
export class App {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionSync = inject(SessionSyncService);
  private readonly supportApi = inject(SupportApiService);
  private readonly router = inject(Router);
  protected readonly schoolContext = inject(SchoolContextService);
  protected readonly unread = inject(UnreadCountsService);
  private readonly linkedAccountsApi = inject(LinkedAccountsApiService);
  private readonly linkAccountDialog = inject(LinkAccountDialogService);
  private readonly bell = viewChild(NotificationBell);

  private readonly confirmDialog = viewChild(ConfirmDialog, {
    read: ElementRef<HTMLElement>,
  });

  private readonly schoolLookup = inject(SchoolLookupService);

  /** The current school when it's a "Vyzkoušet" demo (demo bar, feedback). */
  protected readonly demoSchool = toSignal(
    toObservable(this.schoolContext.slug).pipe(
      switchMap((slug) =>
        slug && isDemoSlug(slug)
          ? this.schoolLookup.lookup(slug).pipe(
              map((school) => (school?.isDemo ? { ...school, slug } : null)),
              catchError(() => of(null)),
            )
          : of(null),
      ),
    ),
    { initialValue: null },
  );

  constructor() {
    installPlaceholderTitles();
    const destroyRef = inject(DestroyRef);
    const feedback = inject(DemoFeedbackService);
    toObservable(this.demoSchool)
      .pipe(takeUntilDestroyed())
      .subscribe((demo) => demo && feedback.start(destroyRef));
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
        // Sidebar "Zprávy" badge and the bell — cheap enough to re-check
        // per navigation.
        this.refreshBadges();
      });
    // Also while someone stays on one page.
    interval(60_000)
      .pipe(
        filter(() => document.visibilityState === 'visible'),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.refreshBadges());
  }

  protected title = 'Soovioo';

  private refreshBadges(): void {
    if (!this.authApi.isLoggedIn() || this.hideHeader()) {
      return;
    }
    if (this.authApi.isSuperAdmin()) {
      this.unread.refreshFeedback();
    } else {
      this.unread.refresh();
    }
  }

  protected toggleBell(event: Event): void {
    this.bell()?.toggle(event);
  }

  protected bellLabel(): string {
    const count = this.unread.notifications();
    return count > 0 ? `Upozornění, ${count} nepřečtených` : 'Upozornění';
  }

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
      { label: 'Lidé', icon: 'class', link: link('people'), familyOnly: true },
      {
        label: 'Mé děti',
        icon: 'children',
        link: link('my-children'),
        parentOnly: true,
      },
      {
        label: 'Administrace',
        icon: 'admin',
        link: link('admin'),
        staffOnly: true,
      },
      { label: 'Logy', icon: 'logs', link: link('logs'), adminOnly: true },
    ];
  });

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  /** Ends support mode: closes the audited session, then the SuperAdmin's
   * own session comes back from the refresh cookie. */
  protected leaveSupport(): void {
    // Full page load back to the SuperAdmin area (main domain in
    // production); its own session comes back from the refresh cookie.
    const backToSuperAdmin = () => (window.location.href = superAdminUrl());
    this.supportApi.endCurrentSession({ withCredentials: true }).subscribe({
      next: backToSuperAdmin,
      error: backToSuperAdmin,
    });
  }

  protected reload(): void {
    window.location.reload();
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

  /** Administrace shows to whoever may manage pupils even in part —
   * AdminPage itself resolves whether they're actually a class teacher of
   * something (restricted view) or not, since that needs an async lookup
   * this synchronous nav check can't do. */
  protected showsNavItem(item: NavItem): boolean {
    if (item.adminOnly) {
      return this.authApi.can('AuditLog');
    }
    if (item.parentOnly) {
      return this.authApi.isParent();
    }
    if (item.familyOnly) {
      return this.authApi.isParent() || this.authApi.isStudent();
    }
    return (
      !item.staffOnly ||
      this.authApi.isAdmin() ||
      this.authApi.can('Students') ||
      canSeeTeachingArea(this.authApi)
    );
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
    return this.authApi.getActiveRole()?.name ?? '';
  }

  /** Cached per roles+active (the roles come from the token, not a
   * signal): p-menu's `@for` tracks items by object reference, so a fresh
   * array on every change-detection cycle tore its `<li>`s down and
   * rebuilt them constantly — a real mouse's click landing in between got
   * dropped. */
  private roleMenuCache: { key: string; items: MenuItem[] } | null = null;

  protected roleMenuItems(): MenuItem[] {
    const roles = this.authApi.getRoles();
    const active = this.authApi.getActiveRole()?.id;
    const key = `${roles.map((r) => `${r.id}:${r.name}`).join(',')}|${active}`;
    if (this.roleMenuCache?.key !== key) {
      this.roleMenuCache = {
        key,
        items: roles.map((role) => ({
          label: role.name,
          icon: role.id === active ? 'pi pi-check' : 'pi pi-fw',
          command: () => this.switchRole(role),
        })),
      };
    }
    return this.roleMenuCache.items;
  }

  /** The user's accounts at other schools, with what's unread there. */
  protected readonly accountMenuItems = computed<MenuItem[]>(() => [
    ...this.unread.linkedAccounts().map((account) => ({
      label:
        account.unread > 0
          ? `${account.schoolName} (${account.unread})`
          : account.schoolName,
      icon: 'pi pi-fw',
      command: () => this.switchAccount(account),
    })),
    ...(this.unread.linkedAccounts().length > 0 ? [{ separator: true }] : []),
    {
      label: 'Propojit další účet',
      icon: 'pi pi-plus',
      command: () => this.linkAccountDialog.open.set(true),
    },
  ]);

  /** A person's own accounts: not for the SuperAdmin, support mode or a
   * demo school, which the API refuses anyway. */
  protected canLinkAccounts(): boolean {
    return (
      !this.isSuperAdmin() &&
      !this.authApi.getSupportInfo() &&
      !this.authApi.mustChangePassword() &&
      !isDemoSlug(this.schoolContext.slug())
    );
  }

  protected readonly linkedUnread = computed(() =>
    this.unread.linkedAccounts().reduce((sum, a) => sum + a.unread, 0),
  );

  /** On to the other school's address; this session stays here. */
  private switchAccount(account: LinkedAccount): void {
    this.linkedAccountsApi.switchTo(account.userId).subscribe({
      next: ({ code, schoolSlug }) => {
        window.location.href = `${schoolUrl(schoolSlug, 'account/switch')}#code=${code}`;
      },
      error: (err) => console.error('switchAccount failed', err),
    });
  }

  /** Every page decides what to show by the role once, when it's created —
   * a full reload onto Home is the simplest way to make all of them
   * re-decide. */
  private switchRole(role: RoleRef): void {
    if (role.id === this.authApi.getActiveRole()?.id) {
      return;
    }
    this.authApi.switchRole(role.id).subscribe({
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

  /** SuperAdmin lives outside the `:schoolSlug` route tree (see
   * app.routes.ts), so its Settings link can't go through
   * `SchoolContextService.link()`. */
  protected settingsLink(): string[] {
    return this.isSuperAdmin()
      ? ['/superadmin/settings']
      : this.schoolContext.link('settings');
  }
}
