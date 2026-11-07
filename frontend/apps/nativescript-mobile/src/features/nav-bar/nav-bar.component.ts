import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  NativeScriptCommonModule,
  NativeScriptRouterModule,
  RouterExtensions,
} from '@nativescript/angular';
import { Dialogs } from '@nativescript/core';
import {
  AuthApiService,
  LinkedAccount,
  LinkedAccountsApiService,
  NotificationsApiService,
} from '@soovioo/shared';
import { switchMap } from 'rxjs';
import { PushService } from '../../push.service';

const LINK_ACCOUNT = 'Propojit další účet';

@Component({
  selector: 'ns-nav-bar',
  templateUrl: './nav-bar.component.html',
  imports: [NativeScriptCommonModule, NativeScriptRouterModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class NavBarComponent {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);
  private readonly notificationsApi = inject(NotificationsApiService);
  private readonly linkedAccountsApi = inject(LinkedAccountsApiService);
  private readonly push = inject(PushService);

  /** Unread notifications — the bar is on every page, so it's checked
   * whenever a page opens, when a push comes and when the app comes back
   * to the front. */
  protected readonly unread = signal(0);

  /** The user's accounts at other schools. */
  protected readonly linked = signal<LinkedAccount[]>([]);
  protected readonly schoolName = this.authApi.getSchoolName();

  constructor() {
    this.loadUnread();
    this.push.changed
      .pipe(takeUntilDestroyed())
      .subscribe(() => this.loadUnread());
  }

  private loadUnread(): void {
    if (!this.authApi.isLoggedIn()) {
      return;
    }
    this.notificationsApi.getUnreadCounts().subscribe({
      next: (counts) => {
        this.unread.set(counts.notifications);
        this.linked.set(counts.linkedAccounts ?? []);
      },
      error: () => this.unread.set(0),
    });
  }

  protected async chooseAccount(): Promise<void> {
    const labels = this.linked().map((account) =>
      account.unread > 0
        ? `${account.schoolName} (${account.unread})`
        : account.schoolName,
    );
    const chosen = await Dialogs.action({
      title: 'Přepnout účet',
      cancelButtonText: 'Zrušit',
      actions: [...labels, LINK_ACCOUNT],
    });
    if (chosen === LINK_ACCOUNT) {
      this.router.navigate(['/settings']);
      return;
    }
    const account = this.linked()[labels.indexOf(chosen)];
    if (account) {
      this.switchTo(account);
    }
  }

  /** The phone stays registered, now for the other account. Pages decide by
   * the account when they're created, and the router wouldn't recreate the
   * one already open. */
  private switchTo(account: LinkedAccount): void {
    this.linkedAccountsApi
      .switchTo(account.userId)
      .pipe(switchMap(({ code }) => this.authApi.redeemLoginCode(code)))
      .subscribe({
        next: () => {
          this.push.enable();
          this.router.navigate(
            [
              this.router.router.url === '/notifications'
                ? '/grades'
                : '/notifications',
            ],
            { clearHistory: true },
          );
        },
        error: () =>
          Dialogs.alert({
            title: 'Přepnutí se nepovedlo',
            message: 'Zkuste to prosím znovu.',
            okButtonText: 'OK',
          }),
      });
  }

  protected logout(): void {
    this.push.disable();
    this.authApi.logout();
    this.router.navigate(['/login'], { clearHistory: true });
  }
}
