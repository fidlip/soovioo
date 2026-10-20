import { Component, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { AuthApiService } from '@soovioo/shared';
import { environment } from '../../environments/environment';
import { SchoolContextService } from '../school-context';
import { SchoolLookupService } from '../school-lookup.service';
import { catchError, map, of } from 'rxjs';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, Button, Card, Checkbox, InputText, Message, Password],
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
   * schoolExistsGuard already asked, so it's usually cached). */
  protected readonly schoolName = toSignal(
    this.schoolSlug
      ? inject(SchoolLookupService)
          .lookup(this.schoolSlug)
          .pipe(
            map((school) => school?.name ?? null),
            catchError(() => of(null)),
          )
      : of(null),
    { initialValue: null },
  );

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
        error: () => this.error.set('Neplatné přihlašovací údaje.'),
      });
  }
}
