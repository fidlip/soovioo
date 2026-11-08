import { Injectable, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router } from '@angular/router';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { SchoolContextService, superAdminUrl } from '../school-context';

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
    this.authApi.sessionExpired$
      .pipe(takeUntilDestroyed())
      .subscribe(() =>
        this.router.navigate(
          this.router.url.startsWith('/superadmin')
            ? ['/superadmin/login']
            : this.schoolContext.link('login'),
        ),
      );
  }

  /** Ends support mode: closes the audited session, then the SuperAdmin's
   * own session comes back from the refresh cookie. */
  leaveSupport(): void {
    // Full page load back to the SuperAdmin area (main domain in
    // production); its own session comes back from the refresh cookie.
    const backToSuperAdmin = () => (window.location.href = superAdminUrl());
    this.supportApi.endCurrentSession({ withCredentials: true }).subscribe({
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
