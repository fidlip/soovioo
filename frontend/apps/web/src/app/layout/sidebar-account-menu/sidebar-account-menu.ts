import { Component, computed, inject } from '@angular/core';
import { Router } from '@angular/router';
import { Menu } from '@openng/optimus-ui/menu';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import { MenuItem } from '@openng/optimus-ui/api';
import {
  AuthApiService,
  LinkedAccount,
  LinkedAccountsApiService,
  RoleRef,
} from '@soovioo/shared';
import { canLinkAccounts } from '../../link-account/can-link-accounts';
import { LinkAccountDialogService } from '../../link-account/link-account-dialog.service';
import { SchoolContextService, schoolUrl } from '../../school-context';
import { UnreadCountsService } from '../../unread-counts.service';
import { SessionActionsService } from '../session-actions.service';
import { checkOnNavigation } from '../check-on-navigation';

/** Bottom of the sidebar: who's logged in, as which role, at which school —
 * with switching between them, and logging out. */
@Component({
  selector: 'app-sidebar-account-menu',
  imports: [Menu, Tooltip],
  templateUrl: './sidebar-account-menu.html',
  styleUrl: './sidebar-account-menu.scss',
})
export class SidebarAccountMenu {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionActions = inject(SessionActionsService);
  private readonly linkAccountDialog = inject(LinkAccountDialogService);
  private readonly unread = inject(UnreadCountsService);
  private readonly linkedAccountsApi = inject(LinkedAccountsApiService);
  private readonly schoolContext = inject(SchoolContextService);
  private readonly router = inject(Router);

  constructor() {
    checkOnNavigation();
  }

  /** Accounts with several roles (e.g. a teacher who is also a parent) get
   * a "Jako: …" switcher. */
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

  protected canLinkAccounts(): boolean {
    return canLinkAccounts(this.authApi, this.schoolContext.slug());
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
}
