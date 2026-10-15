import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { AuthApiService } from '@sova/shared';
import { SchoolContextService } from '../school-context';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule, Button, Card, InputText, Message, Password],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  protected readonly schoolContext = inject(SchoolContextService);

  protected username = '';
  protected password = '';
  protected readonly error = signal<string | null>(null);

  /** Dev/path mode: straight off this route's own `:schoolSlug` segment, so
   * it's correct even before SchoolContextService's NavigationEnd-driven
   * signal has caught up. Subdomain mode: the (static, hostname-derived)
   * service value. */
  protected get schoolSlug(): string | null {
    return this.schoolContext.isPathBased
      ? this.route.snapshot.paramMap.get('schoolSlug')
      : this.schoolContext.slug();
  }

  protected login(): void {
    this.error.set(null);
    this.authApi
      .login({ username: this.username, password: this.password, schoolSlug: this.schoolSlug ?? undefined })
      .subscribe({
        next: (response) =>
          this.router.navigate(response.role === 'SuperAdmin' ? ['/superadmin'] : this.schoolContext.link('grades')),
        error: () => this.error.set('Neplatné přihlašovací údaje.'),
      });
  }
}
