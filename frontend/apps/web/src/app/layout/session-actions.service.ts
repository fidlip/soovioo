import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { SchoolContextService } from '../school-context';
import {
  forgetSupportTab,
  isSupportTab,
} from '../support-enter-page/support-return';

/** Leaving the session: logging out, ending support mode, and back to the
 * login form when the session ends on its own. */
@Injectable({ providedIn: 'root' })
export class SessionActionsService {
  private readonly authApi = inject(AuthApiService);
  private readonly supportApi = inject(SupportApiService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);

  constructor() {
    // The refresh cookie expired or was revoked (logout elsewhere, password
    // change) while this tab was open — back to the login form.
    this.authApi.sessionExpired$.pipe(takeUntilDestroyed()).subscribe(() =>
      // Support mode ended (the school revoked the key, or it expired) —
      // the tab the backoffice opened is done.
      isSupportTab()
        ? this.closeSupportTab()
        : this.router.navigate(
            this.router.url.startsWith('/superadmin')
              ? ['/superadmin/login']
              : this.schoolContext.link('login'),
          ),
    );
  }

  /** Ends support mode: closes the audited session (with what was done,
   * when written), then the tab the backoffice opened is done. */
  leaveSupport(result?: string): void {
    this.supportApi
      .endCurrentSession(result, { withCredentials: true })
      .subscribe({
        next: () => this.closeSupportTab(),
        error: () => this.closeSupportTab(),
      });
  }

  /** Support mode ended by someone else, once the support read why. */
  returnFromSupport(): void {
    this.closeSupportTab();
  }

  /** The tab the backoffice opened closes — the operator's own tab is
   * still at the ticket. One that can't close loads the school's login. */
  private closeSupportTab(): void {
    forgetSupportTab();
    const login = this.router.serializeUrl(
      this.router.createUrlTree(this.schoolContext.link('login')),
    );
    window.close();
    // A full load: nothing of the support's token stays in memory.
    setTimeout(() => (window.location.href = login), 300);
  }

  logout(): void {
    if (this.authApi.getSupportInfo()) {
      // Support mode: leaving ends the audited session.
      this.leaveSupport();
      return;
    }
    const wasSuperAdmin = this.authApi.isSuperAdmin();
    this.authApi.logout();
    this.router.navigate(
      wasSuperAdmin ? ['/superadmin/login'] : this.schoolContext.link('login'),
    );
  }
}
