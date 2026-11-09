import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { AuthApiService, SupportApiService } from '@soovioo/shared';
import { SchoolContextService, superAdminUrl } from '../school-context';
import { rememberSupportReturn } from './support-return';

/** Landing page of "Vstoupit do školy" on the school's own page (its
 * subdomain in production): redeems the one-time code from the URL
 * fragment for a support-mode session, then goes to the school's Home.
 * The fragment's `back` is where "Opustit" returns to. */
@Component({
  selector: 'app-support-enter-page',
  imports: [Button],
  template: `<div class="mx-auto mt-24 max-w-md px-4 text-center">
    @if (error(); as message) {
      <p class="mb-4 text-surface-700 dark:text-surface-200">{{ message }}</p>
      <p-button
        label="Zpět na správu škol"
        icon="pi pi-arrow-left"
        severity="secondary"
        (onClick)="backToSuperAdmin()"
      />
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
    const code = fragment.get('code');
    rememberSupportReturn(fragment.get('back'));
    // The code is single-use anyway — don't leave it in history/address bar.
    history.replaceState(null, '', window.location.pathname);
    if (!code) {
      this.error.set('Chybí kód pro vstup do školy.');
      return;
    }
    this.supportApi.redeem(code, { withCredentials: true }).subscribe({
      next: (response) => {
        this.authApi.store({ token: response.token });
        this.router.navigate(this.schoolContext.link('home'));
      },
      error: () =>
        this.error.set(
          'Odkaz pro vstup do školy je neplatný nebo vypršel. Zkuste to znovu ze správy škol.',
        ),
    });
  }

  protected backToSuperAdmin(): void {
    window.location.href = superAdminUrl();
  }
}
