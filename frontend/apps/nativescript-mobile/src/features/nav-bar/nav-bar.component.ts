import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  NativeScriptCommonModule,
  NativeScriptRouterModule,
  RouterExtensions,
} from '@nativescript/angular';
import { AuthApiService, NotificationsApiService } from '@soovioo/shared';
import { PushService } from '../../push.service';

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
  private readonly push = inject(PushService);

  /** Unread notifications — the bar is on every page, so it's checked
   * whenever a page opens, when a push comes and when the app comes back
   * to the front. */
  protected readonly unread = signal(0);

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
      next: (counts) => this.unread.set(counts.notifications),
      error: () => this.unread.set(0),
    });
  }

  protected logout(): void {
    this.push.disable();
    this.authApi.logout();
    this.router.navigate(['/login'], { clearHistory: true });
  }
}
