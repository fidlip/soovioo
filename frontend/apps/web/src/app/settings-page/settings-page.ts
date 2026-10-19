import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import {
  AuthApiService,
  EventsApiService,
  MyProfile,
  MyProfileApiService,
} from '@soovioo/shared';
import { DarkModeService } from '../dark-mode.service';
import { SchoolContextService } from '../school-context';

import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS } from '../forms';
@Component({
  selector: 'app-settings-page',
  imports: [
    ...FORM_FIELDS,
    InfoHint,
    FormsModule,
    Button,
    Card,
    InputText,
    Message,
    Password,
    ToggleSwitch,
  ],
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  protected readonly notify = inject(NotifyService);
  private readonly profileApi = inject(MyProfileApiService);
  private readonly eventsApi = inject(EventsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly schoolContext = inject(SchoolContextService);
  protected readonly darkModeService = inject(DarkModeService);

  // Captured once at page load, before the password change below could flip
  // the underlying claim to false — otherwise the banner/redirect-on-success
  // would never get a chance to trigger.
  protected readonly wasForcedPasswordChange =
    this.authApi.mustChangePassword();

  protected readonly profile = signal<MyProfile | null>(null);
  protected phone = '';
  protected email = '';

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmNewPassword = '';
  protected readonly passwordError = signal<string | null>(null);

  // Hidden for SuperAdmin: calendar/events are a per-school feature SuperAdmin
  // has no "Kalendář" nav link for either, see app.html.
  protected readonly showCalendarFeed = !this.authApi.isSuperAdmin();
  protected readonly feedUrl = signal<string | null>(null);
  protected readonly feedUrlCopied = signal(false);

  constructor() {
    this.profileApi.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.phone = profile.phone ?? '';
        this.email = profile.email ?? '';
      },
      error: (err) => console.error('getProfile failed', err),
    });

    if (this.showCalendarFeed) {
      this.eventsApi.getCalendarFeedUrl().subscribe({
        next: (result) => this.feedUrl.set(result.feedUrl),
        error: (err) => console.error('getCalendarFeedUrl failed', err),
      });
    }
  }

  protected saveProfile(): void {
    this.profileApi
      .updateProfile({
        phone: this.phone || undefined,
        email: this.email || undefined,
      })
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          this.notify.success('Uloženo.');
        },
        error: (err) => this.notify.actionFailed('updateProfile failed', err),
      });
  }

  protected changePassword(): void {
    this.passwordError.set(null);

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
          this.notify.success('Heslo bylo změněno.');
          this.currentPassword = '';
          this.newPassword = '';
          this.confirmNewPassword = '';
          if (this.wasForcedPasswordChange) {
            // Give the success message a moment on screen, then leave —
            // the forced-change redirect that brought the user here no
            // longer applies once their own password is in place.
            setTimeout(
              () =>
                this.router.navigate(
                  this.authApi.isSuperAdmin()
                    ? ['/superadmin']
                    : this.schoolContext.link('home'),
                ),
              1200,
            );
          }
        },
        error: () => this.passwordError.set('Současné heslo nesouhlasí.'),
      });
  }

  protected copyFeedUrl(): void {
    const url = this.feedUrl();
    if (!url) {
      return;
    }
    navigator.clipboard.writeText(url).then(() => {
      this.feedUrlCopied.set(true);
      setTimeout(() => this.feedUrlCopied.set(false), 2000);
    });
  }

  protected regenerateFeedUrl(): void {
    this.eventsApi.regenerateCalendarFeedUrl().subscribe({
      next: (result) => this.feedUrl.set(result.feedUrl),
      error: (err) => console.error('regenerateCalendarFeedUrl failed', err),
    });
  }
}
