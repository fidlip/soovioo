import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { Select } from '@openng/optimus-ui/select';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import { Subject, catchError, concatMap, of } from 'rxjs';
import {
  AuthApiService,
  EventsApiService,
  MyProfile,
  MyProfileApiService,
  DIGEST_TIMES,
  NOTIFICATION_TYPE_LABELS,
  NotificationSettings,
  NotificationType,
  NotificationsApiService,
  PushQuietMode,
  PushSettingsRequest,
  digestTimeLabel,
  PhotosApiService,
  SetVisibilityRequest,
  StudentPhotoState,
  VisibilitySetting,
} from '@soovioo/shared';
import { DarkModeService } from '../dark-mode.service';
import { SchoolContextService } from '../school-context';

import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS, FloatField } from '../forms';
import { PhotoEditor } from '../photo-editor/photo-editor';
import { StudentPhoto } from '../student-photo/student-photo';
import { VisibilityControl } from '../visibility-control/visibility-control';
@Component({
  selector: 'app-settings-page',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    InfoHint,
    FormsModule,
    Button,
    Card,
    InputText,
    Message,
    Password,
    Select,
    ToggleSwitch,
    PhotoEditor,
    StudentPhoto,
    VisibilityControl,
  ],
  templateUrl: './settings-page.html',
})
export class SettingsPage {
  protected readonly notify = inject(NotifyService);
  private readonly profileApi = inject(MyProfileApiService);
  private readonly eventsApi = inject(EventsApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly notificationsApi = inject(NotificationsApiService);
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
  /** The cards render once the profile request is done (either way): the
   * Profil card filling in afterwards pushed the cards below it down. */
  protected readonly profileSettled = signal(false);
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

  /** Who sees the caller's own data ("Viditelnost údajů"), offered next
   * to each field. */
  protected readonly visibility = signal<VisibilitySetting[]>([]);

  /** A pupil's own photo (parents decide about their children's in Mé
   * děti). */
  protected readonly studentPhotos = signal<StudentPhotoState[]>([]);

  /** Which kinds of notifications the user wants under the bell and in
   * the daily e-mail (all on by default), and when the e-mail comes. */
  protected readonly notificationSettings = signal<NotificationSettings | null>(
    null,
  );
  protected readonly notificationLabels = NOTIFICATION_TYPE_LABELS;
  protected readonly digestTimeOptions = computed(() => {
    const settings = this.notificationSettings();
    return [
      {
        label: `Podle školy (${digestTimeLabel(settings?.schoolDigestTime ?? '')})`,
        value: null as string | null,
      },
      ...DIGEST_TIMES.map((t) => ({ label: t, value: t as string | null })),
    ];
  });
  protected readonly hasForcedEmail = computed(
    () =>
      this.notificationSettings()?.types.some((t) => t.emailForced) ?? false,
  );
  protected readonly digestTime = computed(() => {
    const time = this.notificationSettings()?.digestTime;
    return time ? digestTimeLabel(time) : null;
  });
  protected readonly quietOptions = computed(() => {
    const push = this.notificationSettings()?.push;
    return [
      {
        label: `Podle školy (${digestTimeLabel(push?.schoolQuietFrom ?? '')}–${digestTimeLabel(push?.schoolQuietTo ?? '')})`,
        value: 'School' as PushQuietMode,
      },
      { label: 'Vlastní', value: 'Own' as PushQuietMode },
      { label: 'Bez nočního klidu', value: 'Off' as PushQuietMode },
    ];
  });
  protected readonly quietTimes = DIGEST_TIMES;
  protected readonly label = digestTimeLabel;

  constructor() {
    this.pushSaves
      .pipe(
        concatMap((request) =>
          this.notificationsApi.setPushSettings(request).pipe(
            catchError((err) => {
              this.notify.actionFailed(
                'setNotificationPushSettings failed',
                err,
              );
              return of(null);
            }),
          ),
        ),
        takeUntilDestroyed(),
      )
      .subscribe((saved) => {
        // An older answer would undo what was changed since.
        if (--this.pendingPushSaves === 0 && saved) {
          this.notificationSettings.set(saved);
          this.notify.success('Uloženo.');
        } else if (!saved) {
          this.loadNotificationSettings();
        }
      });
    this.profileApi.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.phone = profile.phone ?? '';
        this.email = profile.email ?? '';
        this.profileSettled.set(true);
      },
      error: (err) => {
        console.error('getProfile failed', err);
        this.profileSettled.set(true);
      },
    });

    if (this.authApi.isStudent()) {
      this.photosApi.getOwnStudents().subscribe({
        // Nothing to decide while the school has pupils' photos off.
        next: (states) =>
          this.studentPhotos.set(states.filter((s) => s.photosEnabled)),
        error: (err) => console.error('getOwnStudents failed', err),
      });
    }

    if (!this.authApi.isSuperAdmin()) {
      this.profileApi.getVisibility().subscribe({
        next: (settings) => this.visibility.set(settings),
        error: (err) => console.error('getVisibility failed', err),
      });
      this.loadNotificationSettings();
    }

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
          // Whether there's an address for the daily e-mail.
          if (!this.authApi.isSuperAdmin()) {
            this.loadNotificationSettings();
          }
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

  protected ownPhotoUrl(profile: MyProfile): string | null {
    return profile.photo
      ? this.photosApi.userPhotoUrl(profile.id, profile.photo, 'profile')
      : null;
  }

  protected uploadOwnPhoto(file: File): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }
    this.photosApi.uploadUserPhoto(profile.id, file, file.name).subscribe({
      next: ({ photo }) => {
        this.profile.set({ ...profile, photo });
        this.notify.success('Fotka nahrána.');
      },
      error: (err) => this.notify.actionFailed('uploadUserPhoto failed', err),
    });
  }

  protected deleteOwnPhoto(): void {
    const profile = this.profile();
    if (!profile) {
      return;
    }
    this.photosApi.deleteUserPhoto(profile.id).subscribe({
      next: () => {
        this.profile.set({ ...profile, photo: null });
        this.notify.success('Fotka smazána.');
      },
      error: (err) => this.notify.actionFailed('deleteUserPhoto failed', err),
    });
  }

  protected studentPhotoChanged(state: StudentPhotoState): void {
    this.studentPhotos.update((all) =>
      all.map((s) => (s.studentId === state.studentId ? state : s)),
    );
  }

  protected setVisibility(request: SetVisibilityRequest): void {
    this.profileApi.setVisibility(request).subscribe({
      next: (settings) => {
        this.visibility.set(settings);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setVisibility failed', err),
    });
  }

  private loadNotificationSettings(): void {
    this.notificationsApi.getSettings().subscribe({
      next: (settings) => this.notificationSettings.set(settings),
      error: (err) => console.error('getNotificationSettings failed', err),
    });
  }

  protected setNotificationType(
    type: NotificationType,
    enabled: boolean,
  ): void {
    const muted = (this.notificationSettings()?.types ?? [])
      .filter((t) => (t.type === type ? !enabled : !t.enabled))
      .map((t) => t.type);
    this.notificationsApi.setSettings(muted).subscribe({
      next: (settings) => {
        this.notificationSettings.set(settings);
        this.notify.success('Uloženo.');
      },
      error: (err) =>
        this.notify.actionFailed('setNotificationSettings failed', err),
    });
  }

  protected setEmailNotifications(
    change: { type: NotificationType; email: boolean } | null,
    digestTime: string | null = this.digestTime(),
  ): void {
    const muted = (this.notificationSettings()?.types ?? [])
      .filter(
        (t) =>
          !t.emailForced &&
          (t.type === change?.type ? !change.email : !t.email),
      )
      .map((t) => t.type);
    this.notificationsApi.setEmailSettings(muted, digestTime).subscribe({
      next: (settings) => {
        this.notificationSettings.set(settings);
        this.notify.success('Uloženo.');
      },
      error: (err) =>
        this.notify.actionFailed('setNotificationEmailSettings failed', err),
    });
  }

  /** One change of the push settings, the rest as it is. */
  protected setPushSettings(
    change: Partial<PushSettingsRequest> & {
      type?: NotificationType;
      enabled?: boolean;
    },
  ): void {
    const settings = this.notificationSettings();
    if (!settings) {
      return;
    }
    const push = settings.push;
    const quiet = change.quiet ?? push.quiet;
    const request: PushSettingsRequest = {
      muted: settings.types
        .filter((t) => (t.type === change.type ? !change.enabled : !t.push))
        .map((t) => t.type),
      fullText: change.fullText ?? push.fullText,
      quiet,
      quietFrom:
        quiet === 'Own'
          ? (change.quietFrom ??
            digestTimeLabel(push.quietFrom ?? push.schoolQuietFrom))
          : null,
      quietTo:
        quiet === 'Own'
          ? (change.quietTo ??
            digestTimeLabel(push.quietTo ?? push.schoolQuietTo))
          : null,
    };
    // Shown right away, so a change made before this one is saved builds
    // on it; the saves go one after another.
    this.notificationSettings.set({
      ...settings,
      types: settings.types.map((t) => ({
        ...t,
        push: !request.muted.includes(t.type),
      })),
      push: {
        ...push,
        fullText: request.fullText,
        quiet,
        quietFrom: request.quietFrom,
        quietTo: request.quietTo,
      },
    });
    this.pendingPushSaves++;
    this.pushSaves.next(request);
  }

  private readonly pushSaves = new Subject<PushSettingsRequest>();
  private pendingPushSaves = 0;

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
