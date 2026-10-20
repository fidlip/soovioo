import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  NativeScriptFormsModule,
  RouterExtensions,
} from '@nativescript/angular';
import { AuthApiService } from '@soovioo/shared';
import { environment } from '../../environments/environment';

@Component({
  selector: 'ns-login',
  templateUrl: './login.component.html',
  imports: [NativeScriptCommonModule, NativeScriptFormsModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class LoginComponent {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);

  protected username = '';
  protected password = '';
  protected schoolSlug = 'demo';
  protected readonly error = signal<string | null>(null);
  /** The well-known demo accounts (Seed:DemoAccounts) exist only in a dev
   * database, so the hint stays out of production builds. */
  protected readonly showDemoAccounts = !environment.production;

  protected login(): void {
    this.error.set(null);
    this.authApi
      .login({
        username: this.username,
        password: this.password,
        schoolSlug: this.schoolSlug.trim() || undefined,
      })
      .subscribe({
        next: () => this.router.navigate(['/grades'], { clearHistory: true }),
        error: () => this.error.set('Neplatné přihlašovací údaje.'),
      });
  }
}
