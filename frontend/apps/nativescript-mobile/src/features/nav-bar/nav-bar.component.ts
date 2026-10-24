import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  NativeScriptRouterModule,
  RouterExtensions,
} from '@nativescript/angular';
import { AuthApiService, NotificationsApiService } from '@soovioo/shared';

@Component({
  selector: 'ns-nav-bar',
  templateUrl: './nav-bar.component.html',
  imports: [NativeScriptCommonModule, NativeScriptRouterModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class NavBarComponent {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);

  /** Unread notifications — the bar is on every page, so it's checked
   * whenever a page opens. */
  protected readonly unread = signal(0);

  constructor() {
    inject(NotificationsApiService)
      .getUnreadCounts()
      .subscribe({
        next: (counts) => this.unread.set(counts.notifications),
        error: () => this.unread.set(0),
      });
  }

  protected logout(): void {
    this.authApi.logout();
    this.router.navigate(['/login'], { clearHistory: true });
  }
}
