import { Component, inject } from '@angular/core';
import { Router, RouterLink, RouterOutlet } from '@angular/router';
import { AuthApiService } from '@sova/shared';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);

  protected title = 'Sova';

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  protected logout(): void {
    this.authApi.logout();
    this.router.navigateByUrl('/login');
  }
}
