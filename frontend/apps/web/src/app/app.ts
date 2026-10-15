import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Button, ButtonDirective } from '@openng/optimus-ui/button';
import { AuthApiService } from '@sova/shared';
import { SchoolContextService } from './school-context';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterLinkActive, RouterOutlet, Button, ButtonDirective],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  protected readonly schoolContext = inject(SchoolContextService);

  protected title = 'Sova';

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  protected logout(): void {
    const wasSuperAdmin = this.authApi.isSuperAdmin();
    this.authApi.logout();
    this.router.navigate(wasSuperAdmin ? ['/superadmin/login'] : this.schoolContext.link('login'));
  }

  protected isAdmin(): boolean {
    return this.authApi.isAdmin();
  }

  protected isSuperAdmin(): boolean {
    return this.authApi.isSuperAdmin();
  }
}
