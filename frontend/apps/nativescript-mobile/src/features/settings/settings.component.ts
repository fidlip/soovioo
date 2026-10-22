import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  NativeScriptFormsModule,
  RouterExtensions,
} from '@nativescript/angular';
import {
  AuthApiService,
  MyProfile,
  MyProfileApiService,
} from '@soovioo/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-settings',
  templateUrl: './settings.component.html',
  imports: [NativeScriptCommonModule, NativeScriptFormsModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class SettingsComponent {
  private readonly profileApi = inject(MyProfileApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);

  // Captured once at page load, before the password change below could flip
  // the underlying claim to false — same as the web Settings page.
  protected readonly wasForcedPasswordChange =
    this.authApi.mustChangePassword();

  protected readonly profile = signal<MyProfile | null>(null);
  protected phone = '';
  protected email = '';
  protected readonly profileMessage = signal<string | null>(null);

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmNewPassword = '';
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordMessage = signal<string | null>(null);

  constructor() {
    this.profileApi.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.phone = profile.phone ?? '';
        this.email = profile.email ?? '';
      },
      error: (err) => console.error('getProfile failed', err),
    });
  }

  protected saveProfile(): void {
    this.profileMessage.set(null);
    this.profileApi
      .updateProfile({
        phone: this.phone || undefined,
        email: this.email || undefined,
      })
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          this.profileMessage.set('Uloženo.');
        },
        error: (err) => {
          console.error('updateProfile failed', err);
          this.profileMessage.set('Uložení se nezdařilo.');
        },
      });
  }

  protected changePassword(): void {
    this.passwordError.set(null);
    this.passwordMessage.set(null);

    if (this.newPassword !== this.confirmNewPassword) {
      this.passwordError.set('Nové heslo a jeho potvrzení se neshodují.');
      return;
    }

    this.profileApi
      .changePassword({
        currentPassword: this.currentPassword,
        newPassword: this.newPassword,
      })
      .subscribe({
        next: () => {
          this.passwordMessage.set('Heslo bylo změněno.');
          this.currentPassword = '';
          this.newPassword = '';
          this.confirmNewPassword = '';
          if (this.wasForcedPasswordChange) {
            // The forced-change redirect that brought the user here no
            // longer applies once their own password is in place.
            setTimeout(
              () => this.router.navigate(['/grades'], { clearHistory: true }),
              1200,
            );
          }
        },
        error: () => this.passwordError.set('Současné heslo nesouhlasí.'),
      });
  }
}
