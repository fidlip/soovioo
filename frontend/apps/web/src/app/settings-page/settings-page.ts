import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
} from '@sova/shared';
import { DarkModeService } from '../dark-mode.service';

@Component({
  selector: 'app-settings-page',
  imports: [
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
  private readonly profileApi = inject(MyProfileApiService);
  private readonly eventsApi = inject(EventsApiService);
  private readonly authApi = inject(AuthApiService);
  protected readonly darkModeService = inject(DarkModeService);

  protected readonly profile = signal<MyProfile | null>(null);
  protected phone = '';
  protected email = '';
  protected readonly profileSaved = signal(false);

  protected currentPassword = '';
  protected newPassword = '';
  protected confirmNewPassword = '';
  protected readonly passwordError = signal<string | null>(null);
  protected readonly passwordChanged = signal(false);

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
    this.profileSaved.set(false);
    this.profileApi
      .updateProfile({
        phone: this.phone || undefined,
        email: this.email || undefined,
      })
      .subscribe({
        next: (profile) => {
          this.profile.set(profile);
          this.profileSaved.set(true);
        },
        error: (err) => console.error('updateProfile failed', err),
      });
  }

  protected changePassword(): void {
    this.passwordError.set(null);
    this.passwordChanged.set(false);

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
          this.passwordChanged.set(true);
          this.currentPassword = '';
          this.newPassword = '';
          this.confirmNewPassword = '';
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
