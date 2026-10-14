import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthApiService } from '@sova/shared';

@Component({
  selector: 'app-login-page',
  imports: [FormsModule],
  templateUrl: './login-page.html',
})
export class LoginPage {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);

  protected username = '';
  protected password = '';
  protected readonly error = signal<string | null>(null);

  protected login(): void {
    this.error.set(null);
    this.authApi.login({ username: this.username, password: this.password }).subscribe({
      next: () => this.router.navigateByUrl('/grades'),
      error: () => this.error.set('Neplatné přihlašovací údaje.'),
    });
  }
}
