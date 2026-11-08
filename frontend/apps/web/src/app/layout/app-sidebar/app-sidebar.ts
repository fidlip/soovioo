import {
  Component,
  ViewEncapsulation,
  computed,
  inject,
  viewChild,
} from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';
import { AppIcon, AppIconName } from '../../app-icon';
import { NotificationBell } from '../../notification-bell/notification-bell';
import { SchoolContextService } from '../../school-context';
import { canSeeTeachingArea } from '../../teaching/teaching-area-access';
import { UnreadCountsService } from '../../unread-counts.service';
import { DeployedVersion } from '../deployed-version/deployed-version';
import { SidebarAccountMenu } from '../sidebar-account-menu/sidebar-account-menu';
import { SidebarStateService } from '../sidebar-state.service';
import { checkOnNavigation } from '../check-on-navigation';

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

/** Sidebar for a logged-in user, with the mobile top bar that opens it as a
 * drawer, and the notification bell. */
@Component({
  selector: 'app-sidebar',
  imports: [
    RouterLink,
    RouterLinkActive,
    AppIcon,
    NotificationBell,
    SidebarAccountMenu,
    DeployedVersion,
  ],
  templateUrl: './app-sidebar.html',
  styleUrl: './app-sidebar.scss',
  encapsulation: ViewEncapsulation.None,
  host: { '(document:keydown.escape)': 'state.drawerOpen.set(false)' },
})
export class AppSidebar {
  protected readonly state = inject(SidebarStateService);
  protected readonly unread = inject(UnreadCountsService);
  private readonly authApi = inject(AuthApiService);
  private readonly schoolContext = inject(SchoolContextService);
  private readonly bell = viewChild(NotificationBell);

  constructor() {
    checkOnNavigation();
  }

  protected toggleBell(event: Event): void {
    this.bell()?.toggle(event);
  }

  protected bellLabel(): string {
    const count = this.unread.notifications();
    return count > 0 ? `Upozornění, ${count} nepřečtených` : 'Upozornění';
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

  protected seesDeployedVersion(): boolean {
    return this.authApi.isAdmin() || this.isSuperAdmin();
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
