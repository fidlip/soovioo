import { Component, NO_ERRORS_SCHEMA, inject } from '@angular/core';
import { NativeScriptCommonModule, NativeScriptRouterModule, RouterExtensions } from '@nativescript/angular';
import { AuthApiService } from '@sova/shared';

@Component({
  selector: 'ns-nav-bar',
  templateUrl: './nav-bar.component.html',
  imports: [NativeScriptCommonModule, NativeScriptRouterModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class NavBarComponent {
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);

  protected logout(): void {
    this.authApi.logout();
    this.router.navigate(['/login'], { clearHistory: true });
  }
}
