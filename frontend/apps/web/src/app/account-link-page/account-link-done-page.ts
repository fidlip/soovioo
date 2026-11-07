import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { AuthApiService, LinkedAccountsApiService } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { SchoolContextService } from '../school-context';

/** Linking, back at the school where it started: the API finishes it only
 * for the account that asked, so a code that came from somebody else links
 * nothing. */
@Component({
  selector: 'app-account-link-done-page',
  imports: [Button],
  template: `<div class="mx-auto mt-24 max-w-md px-4 text-center">
    @if (error(); as message) {
      <p class="mb-4 text-surface-700 dark:text-surface-200">{{ message }}</p>
      <p-button
        [label]="authApi.isLoggedIn() ? 'Do Nastavení' : 'Na přihlášení'"
        severity="secondary"
        (onClick)="leave()"
      />
    } @else {
      <p class="text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2"></i>Propojování účtů…
      </p>
    }
  </div>`,
})
export class AccountLinkDonePage {
  private readonly api = inject(LinkedAccountsApiService);
  protected readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);

  protected readonly error = signal<string | null>(null);

  constructor() {
    const fragment = inject(ActivatedRoute).snapshot.fragment ?? '';
    const code = new URLSearchParams(fragment).get('code');
    history.replaceState(null, '', window.location.pathname);
    if (!code) {
      this.error.set('Chybí kód žádosti o propojení.');
    } else if (!this.authApi.isLoggedIn()) {
      this.error.set(
        'Přihlášení na této škole vypršelo. Přihlaste se a propojte účty znovu.',
      );
    } else {
      this.api.completeRequest(code).subscribe({
        next: () => {
          this.notify.success('Účty jsou propojené.');
          this.leave();
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(
            typeof err.error === 'string'
              ? err.error
              : 'Propojení se nepovedlo.',
          ),
      });
    }
  }

  protected leave(): void {
    this.router.navigate(
      this.schoolContext.link(this.authApi.isLoggedIn() ? 'settings' : 'login'),
    );
  }
}
