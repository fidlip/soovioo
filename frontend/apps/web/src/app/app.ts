import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Button, ButtonDirective } from '@openng/optimus-ui/button';
import { AuthApiService } from '@sova/shared';
import { SchoolContextService } from './school-context';

const DARK_MODE_STORAGE_KEY = 'sova-dark-mode';

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

  // Persisted choice wins; first-ever visit falls back to the OS
  // preference instead of always defaulting to light.
  protected readonly darkMode = signal(this.readInitialDarkMode());

  constructor() {
    this.applyDarkModeClass(this.darkMode());
  }

  protected toggleDarkMode(): void {
    const next = !this.darkMode();
    this.darkMode.set(next);
    this.applyDarkModeClass(next);
    try {
      localStorage.setItem(DARK_MODE_STORAGE_KEY, String(next));
    } catch {
      // Private browsing / blocked storage — the toggle still works for
      // this page load, it just won't survive a reload.
    }
  }

  private readInitialDarkMode(): boolean {
    try {
      const stored = localStorage.getItem(DARK_MODE_STORAGE_KEY);
      if (stored !== null) {
        return stored === 'true';
      }
    } catch {
      // Fall through to the OS preference below.
    }
    return window.matchMedia?.('(prefers-color-scheme: dark)').matches ?? false;
  }

  private applyDarkModeClass(enabled: boolean): void {
    document.documentElement.classList.toggle('sova-dark', enabled);
  }

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
