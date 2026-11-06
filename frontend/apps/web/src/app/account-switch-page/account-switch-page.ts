import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { AuthApiService } from '@soovioo/shared';
import { SchoolContextService } from '../school-context';

/** Where switching to a linked account lands, on that school's own page
 * (its subdomain in production): redeems the one-time code from the URL
 * fragment for a session here, then goes to the school's Home. */
@Component({
  selector: 'app-account-switch-page',
  imports: [Button, RouterLink],
  template: `<div class="mx-auto mt-24 max-w-md px-4 text-center">
    @if (error(); as message) {
      <p class="mb-4 text-surface-700 dark:text-surface-200">{{ message }}</p>
      <a [routerLink]="schoolContext.link('login')">
        <p-button label="Přihlásit se" />
      </a>
    } @else {
      <p class="text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2"></i>Přepínání účtu…
      </p>
    }
  </div>`,
})
export class AccountSwitchPage {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  protected readonly schoolContext = inject(SchoolContextService);

  protected readonly error = signal<string | null>(null);

  constructor() {
    const code = new URLSearchParams(window.location.hash.slice(1)).get('code');
    // Single-use anyway — don't leave it in history/address bar.
    history.replaceState(null, '', window.location.pathname);
    if (!code) {
      this.error.set('Chybí kód pro přepnutí účtu.');
      return;
    }
    this.authApi.redeemLoginCode(code).subscribe({
      next: () => this.router.navigate(this.schoolContext.link('home')),
      error: () =>
        this.error.set(
          'Přepnutí na druhý účet se nepovedlo. Přihlaste se prosím.',
        ),
    });
  }
}
