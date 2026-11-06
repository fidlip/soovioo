import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  NativeScriptFormsModule,
  RouterExtensions,
} from '@nativescript/angular';
import { requestPermissions, takePicture } from '@nativescript/camera';
import { ImageSource } from '@nativescript/core';
import {
  AuthApiService,
  MyProfile,
  MyProfileApiService,
  NOTIFICATION_TYPE_LABELS,
  NotificationType,
  NotificationSettings,
  NotificationTypeSetting,
  NotificationsApiService,
  PushSettingsRequest,
  digestTimeLabel,
  PhotosApiService,
  StudentPhotoState,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { NavBarComponent } from '../nav-bar/nav-bar.component';
import { LinkedAccountsSectionComponent } from './linked-accounts-section.component';

@Component({
  selector: 'ns-settings',
  templateUrl: './settings.component.html',
  imports: [
    NativeScriptCommonModule,
    NativeScriptFormsModule,
    NavBarComponent,
    LinkedAccountsSectionComponent,
  ],
  schemas: [NO_ERRORS_SCHEMA],
})
export class SettingsComponent {
  private readonly profileApi = inject(MyProfileApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly notificationsApi = inject(NotificationsApiService);
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

  /** Own photo (staff, parents) — or, for a pupil, their pupil photo,
   * which waits for a parent's approval. */
  protected readonly photo = signal<ImageSource | null>(null);
  protected readonly ownStudent = signal<StudentPhotoState | null>(null);
  protected readonly photoMessage = signal<string | null>(null);

  protected readonly notificationTypes = signal<NotificationTypeSetting[]>([]);
  protected readonly notificationLabels = NOTIFICATION_TYPE_LABELS;
  protected readonly notificationSettings = signal<NotificationSettings | null>(
    null,
  );
  protected readonly time = digestTimeLabel;

  constructor() {
    this.profileApi.getProfile().subscribe({
      next: (profile) => {
        this.profile.set(profile);
        this.phone = profile.phone ?? '';
        this.email = profile.email ?? '';
        if (profile.photo) {
          this.loadPhoto(
            this.photosApi.userPhotoUrl(profile.id, profile.photo, 'profile'),
          );
        }
      },
      error: (err) => console.error('getProfile failed', err),
    });
    this.notificationsApi.getSettings().subscribe({
      next: (settings) => this.setNotificationSettings(settings),
      error: (err) => console.error('getNotificationSettings failed', err),
    });
    if (this.authApi.isStudent()) {
      this.photosApi.getOwnStudents().subscribe({
        next: (states) => this.setOwnStudent(states[0] ?? null),
        error: (err) => console.error('getOwnStudents failed', err),
      });
    }
  }

  protected setNotificationType(
    type: NotificationType,
    enabled: boolean,
  ): void {
    const current = this.notificationTypes();
    // The Switch also fires when the list first renders it.
    if (current.find((t) => t.type === type)?.enabled === enabled) {
      return;
    }
    const muted = current
      .filter((t) => (t.type === type ? !enabled : !t.enabled))
      .map((t) => t.type);
    this.notificationsApi.setSettings(muted).subscribe({
      next: (settings) => this.setNotificationSettings(settings),
      error: (err) => console.error('setNotificationSettings failed', err),
    });
  }

  private setNotificationSettings(settings: NotificationSettings): void {
    this.notificationSettings.set(settings);
    this.notificationTypes.set(settings.types);
  }

  /** One change of the push settings, the rest as it is. The night quiet
   * is only on (the school's, or own times set on the web) or off here. */
  protected setPush(change: {
    type?: NotificationType;
    enabled?: boolean;
    fullText?: boolean;
    quiet?: boolean;
  }): void {
    const settings = this.notificationSettings();
    if (!settings) {
      return;
    }
    const push = settings.push;
    const quietOn = push.quiet !== 'Off';
    // The Switch also fires when the list first renders it.
    if (
      (change.type &&
        settings.types.find((t) => t.type === change.type)?.push ===
          change.enabled) ||
      (change.fullText !== undefined && change.fullText === push.fullText) ||
      (change.quiet !== undefined && change.quiet === quietOn)
    ) {
      return;
    }
    const quiet =
      change.quiet === undefined ? push.quiet : change.quiet ? 'School' : 'Off';
    const request: PushSettingsRequest = {
      muted: settings.types
        .filter((t) => (t.type === change.type ? !change.enabled : !t.push))
        .map((t) => t.type),
      fullText: change.fullText ?? push.fullText,
      quiet,
      quietFrom: quiet === 'Own' ? push.quietFrom : null,
      quietTo: quiet === 'Own' ? push.quietTo : null,
    };
    this.notificationsApi.setPushSettings(request).subscribe({
      next: (saved) => this.setNotificationSettings(saved),
      error: (err) => console.error('setPushSettings failed', err),
    });
  }

  protected quietLabel(settings: NotificationSettings): string {
    const push = settings.push;
    return push.quiet === 'Own'
      ? `Noční klid ${this.time(push.quietFrom ?? '')}–${this.time(push.quietTo ?? '')}`
      : `Noční klid ${this.time(push.schoolQuietFrom)}–${this.time(push.schoolQuietTo)}`;
  }

  protected canTakePhoto(): boolean {
    return this.authApi.isStudent()
      ? (this.ownStudent()?.canUpload ?? false)
      : (this.profile()?.canUploadPhoto ?? false);
  }

  /** Camera → JPEG → upload; the server crops, shrinks and strips it. */
  protected async takePhoto(): Promise<void> {
    const profile = this.profile();
    if (!profile) {
      return;
    }
    this.photoMessage.set(null);
    try {
      await requestPermissions();
      const asset = await takePicture({
        width: 1024,
        height: 1024,
        keepAspectRatio: true,
        saveToGallery: false,
      });
      // The plugin's ImageAsset type comes from the workspace's copy of
      // @nativescript/core, the app's from its own — same class at runtime.
      const image = await ImageSource.fromAsset(
        asset as unknown as Parameters<typeof ImageSource.fromAsset>[0],
      );
      const jpeg = base64ToBytes(image.toBase64String('jpg', 85));
      const student = this.ownStudent();
      const upload: Observable<unknown> = student
        ? this.photosApi.uploadStudentPhotoBytes(student.studentId, jpeg)
        : this.photosApi.uploadUserPhotoBytes(profile.id, jpeg);
      upload.subscribe({
        next: (result) => {
          if (student) {
            // "Čeká na schválení" shows from the state itself.
            this.setOwnStudent(result as StudentPhotoState);
          } else {
            const { photo } = result as { photo: string | null };
            this.profile.set({ ...profile, photo });
            if (photo) {
              this.loadPhoto(
                this.photosApi.userPhotoUrl(profile.id, photo, 'profile'),
              );
            }
            this.photoMessage.set('Fotka nahrána.');
          }
        },
        error: (err) => {
          console.error('photo upload failed', err);
          this.photoMessage.set('Fotku se nepodařilo nahrát.');
        },
      });
    } catch (err) {
      // Cancelled, or no camera permission.
      console.log('takePhoto cancelled', err);
    }
  }

  private setOwnStudent(state: StudentPhotoState | null): void {
    this.ownStudent.set(state);
    if (state?.photo) {
      this.loadPhoto(
        this.photosApi.studentPhotoUrl(state.studentId, state.photo, 'profile'),
      );
    }
  }

  private loadPhoto(url: string): void {
    this.photosApi.getPhotoBytes(url).subscribe({
      next: (bytes) =>
        this.photo.set(ImageSource.fromBase64Sync(bytesToBase64(bytes))),
      error: (err) => console.error('getPhoto failed', err),
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

const BASE64 =
  'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/';

/** Base64 by hand both ways: NativeScript's `atob`/`btoa` treat the
 * string as UTF-8, so bytes above 127 come out wrong. */
function base64ToBytes(base64: string): Uint8Array {
  const clean = base64.replace(/[^A-Za-z0-9+/]/g, '');
  const bytes = new Uint8Array(Math.floor((clean.length * 3) / 4));
  let o = 0;
  for (let i = 0; i < clean.length; i += 4) {
    const n =
      (BASE64.indexOf(clean[i]) << 18) |
      (BASE64.indexOf(clean[i + 1]) << 12) |
      ((i + 2 < clean.length ? BASE64.indexOf(clean[i + 2]) : 0) << 6) |
      (i + 3 < clean.length ? BASE64.indexOf(clean[i + 3]) : 0);
    bytes[o++] = (n >> 16) & 255;
    if (i + 2 < clean.length) bytes[o++] = (n >> 8) & 255;
    if (i + 3 < clean.length) bytes[o++] = n & 255;
  }
  return bytes.subarray(0, o);
}

function bytesToBase64(buffer: ArrayBuffer): string {
  const alphabet = BASE64;
  const bytes = new Uint8Array(buffer);
  let out = '';
  for (let i = 0; i < bytes.length; i += 3) {
    const n =
      (bytes[i] << 16) | ((bytes[i + 1] ?? 0) << 8) | (bytes[i + 2] ?? 0);
    out +=
      alphabet[(n >> 18) & 63] +
      alphabet[(n >> 12) & 63] +
      (i + 1 < bytes.length ? alphabet[(n >> 6) & 63] : '=') +
      (i + 2 < bytes.length ? alphabet[n & 63] : '=');
  }
  return out;
}
