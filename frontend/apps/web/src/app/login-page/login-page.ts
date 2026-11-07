import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import {
  AuthApiService,
  DemoApiService,
  PublicSchool,
  LinkRequestPreview,
  LinkedAccountsApiService,
} from '@soovioo/shared';
import { environment } from '../../environments/environment';
import { DemoUserPicker } from '../demo/demo-user-picker';
import { SchoolContextService, isDemoSlug } from '../school-context';
import { SchoolLookupService } from '../school-lookup.service';
import { catchError, of } from 'rxjs';
import { FloatField } from '../forms';

@Component({
  selector: 'app-login-page',
  imports: [
    FloatField,
    FormsModule,
    Button,
    Card,
    Checkbox,
    InputText,
    Message,
    Password,
    DemoUserPicker,
  ],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly schoolContext = inject(SchoolContextService);

  protected username = '';
  protected password = '';
  /** Off by default: a shared school/family computer shouldn't keep the
   * last person signed in for 30 days unless they ask for it. */
  protected rememberMe = false;
  protected readonly error = signal<string | null>(null);
  /** The well-known demo accounts (Seed:DemoAccounts) exist only in a dev
   * database, so the hint stays out of production builds. */
  protected readonly showDemoAccounts = !environment.production;

  /** Dev/path mode: straight off this route's own `:schoolSlug` segment, so
   * it's correct even before SchoolContextService's NavigationEnd-driven
   * signal has caught up. Subdomain mode: the (static, hostname-derived)
   * service value. */
  protected get schoolSlug(): string | null {
    return this.schoolContext.isPathBased
      ? this.route.snapshot.paramMap.get('schoolSlug')
      : this.schoolContext.slug();
  }

  /** The school's real name under the heading (the slug until it loads;
   * schoolExistsGuard already asked, so it's usually cached) — and whether
   * it's a demo, which swaps the form for a user picker. */
  protected readonly school = toSignal(
    this.schoolSlug
      ? inject(SchoolLookupService)
          .lookup(this.schoolSlug)
          .pipe(catchError(() => of(null)))
      : of(null),
    { initialValue: null as PublicSchool | null },
  );

  protected readonly schoolName = computed(() => this.school()?.name ?? null);

  /** Came to log in for linking this account with another school's
   * (`?link=1#code=…`, from AccountLinkPage), which it goes back to. */
  private readonly linkFragment = this.route.snapshot.queryParamMap.has('link')
    ? (this.route.snapshot.fragment ?? '')
    : null;
  protected readonly linkRequest = toSignal(
    this.linkFragment === null
      ? of(null)
      : inject(LinkedAccountsApiService)
          .previewRequest(
            new URLSearchParams(this.linkFragment).get('code') ?? '',
          )
          .pipe(catchError(() => of(null))),
    { initialValue: null as LinkRequestPreview | null },
  );

  /** The demo wizard sends the visitor here with `?user=<id>` — log in
   * straight away; the picker shows meanwhile (and if that fails). */
  protected readonly autoLoginPending = signal(false);

  constructor() {
    const userId = Number(this.route.snapshot.queryParamMap.get('user'));
    const slug = this.schoolSlug;
    if (slug && isDemoSlug(slug) && Number.isInteger(userId) && userId > 0) {
      this.autoLoginPending.set(true);
      inject(DemoApiService)
        .login(slug, userId)
        .subscribe({
          next: () => this.enterDemo(),
          error: () => this.autoLoginPending.set(false),
        });
    }
  }

  /** Full reload onto Home: every page decides by the role once, when
   * it's created — same as switching roles. */
  protected enterDemo(): void {
    window.location.assign(
      this.router.serializeUrl(
        this.router.createUrlTree(this.schoolContext.link('home')),
      ),
    );
  }

  protected login(): void {
    this.error.set(null);
    this.authApi
      .login({
        username: this.username,
        password: this.password,
        schoolSlug: this.schoolSlug ?? undefined,
        rememberMe: this.rememberMe,
      })
      .subscribe({
        next: (response) => {
          if (this.linkFragment !== null) {
            this.router.navigate(
              [...this.schoolContext.link('account'), 'link'],
              { fragment: this.linkFragment },
            );
            return;
          }
          if (this.authApi.mustChangePassword()) {
            this.router.navigate(
              response.role === 'SuperAdmin'
                ? ['/superadmin', 'settings']
                : this.schoolContext.link('settings'),
            );
            return;
          }
          this.router.navigate(
            response.role === 'SuperAdmin'
              ? ['/superadmin']
              : this.schoolContext.link('home'),
          );
        },
        error: (err: unknown) =>
          this.error.set(
            // 403 = right password, but the account was deactivated.
            err instanceof HttpErrorResponse &&
              err.status === 403 &&
              typeof err.error === 'string'
              ? err.error
              : 'Neplatné přihlašovací údaje.',
          ),
      });
  }
}
