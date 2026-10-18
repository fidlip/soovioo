import { Component, computed, inject } from '@angular/core';
import {
  Router,
  RouterLink,
  RouterLinkActive,
  RouterOutlet,
} from '@angular/router';
import { Button, ButtonDirective } from '@openng/optimus-ui/button';
import { Menu } from '@openng/optimus-ui/menu';
import { MenuItem } from '@openng/optimus-ui/api';
import { AuthApiService } from '@soovioo/shared';
import { DarkModeService } from './dark-mode.service';
import { SchoolContextService } from './school-context';

@Component({
  selector: 'app-root',
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    Button,
    ButtonDirective,
    Menu,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly darkModeService = inject(DarkModeService);
  protected readonly schoolContext = inject(SchoolContextService);

  protected title = 'Soovioo';

  protected readonly darkMode = this.darkModeService.darkMode;

  protected toggleDarkMode(): void {
    this.darkModeService.toggle();
  }

  protected isLoggedIn(): boolean {
    return this.authApi.isLoggedIn();
  }

  protected logout(): void {
    const wasSuperAdmin = this.authApi.isSuperAdmin();
    this.authApi.logout();
    this.router.navigate(
      wasSuperAdmin ? ['/superadmin/login'] : this.schoolContext.link('login'),
    );
  }

  protected isAdmin(): boolean {
    return this.authApi.isAdmin();
  }

  /** Shows the Administrace nav link to any Teacher, not just class
   * teachers — AdminPage itself resolves whether they're actually a class
   * teacher of something (restricted view) or not (same "jen pro ... "
   * denial as before), since that needs an async lookup this synchronous
   * nav check can't do. */
  protected isTeacher(): boolean {
    return this.authApi.isTeacher();
  }

  protected isSuperAdmin(): boolean {
    return this.authApi.isSuperAdmin();
  }

  protected username(): string | undefined {
    return this.authApi.getUsername() ?? undefined;
  }

  /** SuperAdmin lives outside the `:schoolSlug` route tree (see app.routes.ts),
   * so its Settings link can't go through `SchoolContextService.link()`.
   *
   * Must be a `computed()`, not a plain method called from the template
   * (`[model]="userMenuItems()"`): p-menu's `@for` tracks items by object
   * reference, so a method re-run on every change-detection cycle handed it
   * a brand-new array/objects each time, tearing the `<li>`/`<a>` elements
   * down and rebuilding them constantly. If that teardown landed between a
   * real mouse's mousedown and mouseup — never between a test's synthetic
   * click() — the browser dropped the click, which is why the first click
   * on a menu item did nothing and only the second one (hitting a settled
   * DOM) worked. `computed()` caches the array and only rebuilds it when
   * `schoolContext.link()`'s underlying slug signal actually changes. */
  protected readonly userMenuItems = computed<MenuItem[]>(() => {
    const settingsLink = this.isSuperAdmin()
      ? ['/superadmin/settings']
      : this.schoolContext.link('settings');
    return [
      { label: 'Nastavení', icon: 'pi pi-cog', routerLink: settingsLink },
      { separator: true },
      {
        label: 'Odhlásit se',
        icon: 'pi pi-sign-out',
        command: () => this.logout(),
      },
    ];
  });
}
