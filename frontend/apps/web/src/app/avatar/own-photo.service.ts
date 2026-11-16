import { Injectable, computed, inject, signal } from '@angular/core';
import {
  AuthApiService,
  MyProfileApiService,
  PhotosApiService,
} from '@soovioo/shared';

/** The logged-in user's own photo, for the sidebar. Nastavení passes a new
 * or deleted photo on, so the sidebar follows without a reload. */
@Injectable({ providedIn: 'root' })
export class OwnPhotoService {
  private readonly authApi = inject(AuthApiService);
  private readonly profileApi = inject(MyProfileApiService);
  private readonly photosApi = inject(PhotosApiService);

  private readonly token = signal<string | null>(null);
  private readonly userId = signal<number | null>(null);

  readonly url = computed(() => {
    const token = this.token();
    const userId = this.userId();
    return token && userId !== null
      ? this.photosApi.userPhotoUrl(userId, token)
      : null;
  });

  load(): void {
    this.set(null);
    this.profileApi.getProfile().subscribe({
      next: (profile) => this.set(profile.photo),
      error: (err) => console.error('getProfile failed', err),
    });
  }

  /** `token` = `MyProfile.photo`, null = no photo. */
  set(token: string | null): void {
    this.userId.set(this.authApi.getUserId());
    this.token.set(token);
  }
}
