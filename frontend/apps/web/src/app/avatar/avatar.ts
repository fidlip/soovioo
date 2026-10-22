import {
  Component,
  DestroyRef,
  computed,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { PhotosApiService } from '@soovioo/shared';

/** Round photo, or the person's initials in a circle where there's none
 * (or it can't be loaded). `photoUrl` comes from `PhotosApiService` — the
 * photo is fetched with the session's token, not by `<img src>`. */
@Component({
  selector: 'app-avatar',
  template: `@if (objectUrl(); as url) {
      <img
        [src]="url"
        [alt]="name()"
        class="h-full w-full rounded-full object-cover"
      />
    } @else {
      <span aria-hidden="true">{{ initials() }}</span>
    }`,
  host: {
    class:
      'inline-flex shrink-0 items-center justify-center overflow-hidden rounded-full bg-primary-100 font-semibold text-primary-700 dark:bg-primary-900 dark:text-primary-200 select-none',
    '[style.width.px]': 'size()',
    '[style.height.px]': 'size()',
    '[style.font-size.px]': 'size() * 0.4',
    '[attr.title]': 'name()',
  },
})
export class Avatar {
  private readonly photosApi = inject(PhotosApiService);

  readonly name = input.required<string>();
  readonly photoUrl = input<string | null | undefined>(null);
  /** Diameter in px. */
  readonly size = input(32);

  protected readonly objectUrl = signal<string | null>(null);

  protected readonly initials = computed(() =>
    this.name()
      .split(/\s+/)
      .filter((part) => part.length > 0)
      .slice(0, 2)
      .map((part) => part[0].toUpperCase())
      .join(''),
  );

  constructor() {
    let current: string | null = null;
    const release = () => {
      if (current) {
        URL.revokeObjectURL(current);
        current = null;
      }
    };
    inject(DestroyRef).onDestroy(release);

    effect((onCleanup) => {
      const url = this.photoUrl();
      if (!url) {
        release();
        this.objectUrl.set(null);
        return;
      }
      const subscription = this.photosApi.getPhoto(url).subscribe({
        next: (blob) => {
          release();
          current = URL.createObjectURL(blob);
          this.objectUrl.set(current);
        },
        // Initials stay — a photo that can't be loaded isn't worth a toast.
        error: () => this.objectUrl.set(null),
      });
      onCleanup(() => subscription.unsubscribe());
    });
  }
}
