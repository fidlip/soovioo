import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { SchoolContextService } from '../school-context';
import { rememberSupportReturn } from './support-return';

/** Landing page of the backoffice's "Vstoupit" on the school's own page
 * (its subdomain in production): posts the entry from the URL fragment —
 * the school's one-time challenge signed with the key the school approved —
 * for a support-mode session, then goes to the school's Home. */
@Component({
  selector: 'app-support-enter-page',
  imports: [],
  template: `<div class="mx-auto mt-24 max-w-md px-4 text-center">
    @if (error(); as message) {
      <p class="mb-4 text-surface-700 dark:text-surface-200">{{ message }}</p>
    } @else {
      <p class="text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2"></i>Vstup do školy…
      </p>
    }
  </div>`,
})
export class SupportEnterPage {
  private readonly supportApi = inject(SupportApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);

  protected readonly error = signal<string | null>(null);

  constructor() {
    const fragment = new URLSearchParams(window.location.hash.slice(1));
    const entry = fragment.get('entry');
    rememberSupportReturn(null);
    // Single-use anyway — don't leave it in history/address bar.
    history.replaceState(null, '', window.location.pathname);
    if (!entry) {
      this.error.set('Chybí údaje pro vstup do školy.');
      return;
    }
    this.supportApi.enter(entry, { withCredentials: true }).subscribe({
      next: (response) => {
        this.authApi.store({ token: response.token });
        this.router.navigate(this.schoolContext.link('home'));
      },
      error: () =>
        this.error.set(
          'Odkaz pro vstup do školy je neplatný nebo vypršel. Zkuste to znovu z backoffice.',
        ),
    });
  }
}
