import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { SchoolContextService, superAdminUrl } from '../school-context';
import {
  hasSupportReturn,
  supportReturnPath,
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
      // Support mode ended (the school took the approval back, or it
      // expired) — back to the SuperAdmin page entering started from.
      hasSupportReturn()
        ? (window.location.href = superAdminUrl(supportReturnPath()))
        : this.router.navigate(
            this.router.url.startsWith('/superadmin')
              ? ['/superadmin/login']
              : this.schoolContext.link('login'),
          ),
    );
  }

  /** Ends support mode: closes the audited session (with what was done,
   * when written), then the SuperAdmin's own session comes back from the
   * refresh cookie. */
  leaveSupport(result?: string): void {
    // Full page load back to the SuperAdmin page entering started from
    // (main domain in production); its own session comes back from the
    // refresh cookie.
    const backToSuperAdmin = () =>
      (window.location.href = superAdminUrl(supportReturnPath()));
    this.supportApi
      .endCurrentSession(result, { withCredentials: true })
      .subscribe({
        next: backToSuperAdmin,
        error: backToSuperAdmin,
      });
  }

  logout(): void {
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
}
