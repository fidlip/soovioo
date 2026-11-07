import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import {
  AuthApiService,
  LinkRequestPreview,
  LinkedAccountsApiService,
} from '@soovioo/shared';
import { SchoolContextService, schoolUrl } from '../school-context';

/** Linking, at the other school: whoever isn't logged in here goes through
 * this school's own login and comes back; then they confirm in so many words
 * which two accounts get linked. Finished back at the first school. */
@Component({
  selector: 'app-account-link-page',
  imports: [Button],
  template: `<div class="mx-auto mt-24 max-w-md px-4 text-center">
    @if (error(); as message) {
      <p class="mb-4 text-surface-700 dark:text-surface-200">{{ message }}</p>
      <p-button
        [label]="preview() ? 'Zpět' : 'Na přihlášení'"
        severity="secondary"
        (onClick)="leave()"
      />
    } @else if (preview(); as request) {
      <h1 class="mb-4 text-xl font-semibold">Propojit účty?</h1>
      <p class="mb-2 text-surface-700 dark:text-surface-200">
        Účet <strong>{{ username }}</strong> ({{ schoolName }}) se propojí s
        účtem <strong>{{ request.username }}</strong> ({{
          request.schoolName
        }}).
      </p>
      <p class="mb-6 text-sm text-surface-500">
        Mezi propojenými účty se přepíná bez hesla. Potvrďte jen tehdy, když oba
        účty patří vám.
      </p>
      <div class="flex flex-wrap justify-center gap-2">
        <p-button label="Propojit" (onClick)="approve()" />
        <p-button
          label="Použít jiný účet"
          severity="secondary"
          [outlined]="true"
          (onClick)="otherAccount()"
        />
        <p-button
          label="Zrušit"
          severity="secondary"
          [text]="true"
          (onClick)="leave()"
        />
      </div>
    } @else {
      <p class="text-surface-500">
        <i class="pi pi-spin pi-spinner mr-2"></i>Načítání…
      </p>
    }
  </div>`,
})
export class AccountLinkPage {
  private readonly api = inject(LinkedAccountsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);

  private readonly fragment = inject(ActivatedRoute).snapshot.fragment ?? '';
  private readonly code = new URLSearchParams(this.fragment).get('code');

  protected readonly username = this.authApi.getUsername();
  protected readonly schoolName = this.authApi.getSchoolName();
  protected readonly preview = signal<LinkRequestPreview | null>(null);
  protected readonly error = signal<string | null>(null);

  constructor() {
    if (!this.code) {
      this.error.set('Chybí kód žádosti o propojení.');
    } else if (!this.authApi.isLoggedIn()) {
      this.toLogin();
    } else {
      this.api.previewRequest(this.code).subscribe({
        next: (preview) => this.preview.set(preview),
        error: (err) => this.fail(err),
      });
    }
  }

  protected approve(): void {
    this.api.approveRequest(this.code!).subscribe({
      next: ({ code, schoolSlug }) => {
        window.location.href = `${schoolUrl(schoolSlug, 'account/link/done')}#code=${code}`;
      },
      error: (err) => this.fail(err),
    });
  }

  protected otherAccount(): void {
    this.authApi.logout();
    this.toLogin();
  }

  /** Back to the school that asked, or here when it's not known. */
  protected leave(): void {
    const request = this.preview();
    if (request) {
      window.location.href = schoolUrl(request.schoolSlug, 'settings');
    } else {
      this.router.navigate(this.schoolContext.link('login'));
    }
  }

  private toLogin(): void {
    this.router.navigate(this.schoolContext.link('login'), {
      queryParams: { link: 1 },
      fragment: this.fragment,
    });
  }

  private fail(err: HttpErrorResponse): void {
    this.error.set(
      typeof err.error === 'string' ? err.error : 'Propojení se nepovedlo.',
    );
  }
}
