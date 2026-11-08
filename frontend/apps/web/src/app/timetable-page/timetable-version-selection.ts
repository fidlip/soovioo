import { Injectable, computed, inject, signal } from '@angular/core';
import {
  TimetableVersion,
  TimetableVersionsApiService,
  timetableVersionOn,
} from '@soovioo/shared';
import { toDateOnly } from '../departures/retention-labels';

/** Administrace → Výuka: the timetable version all its sections work with. */
@Injectable()
export class TimetableVersionSelection {
  private readonly api = inject(TimetableVersionsApiService);

  readonly versions = signal<TimetableVersion[]>([]);
  /** null while they aren't in, or for a school without a timetable. */
  readonly versionId = signal<number | null>(null);
  readonly version = computed(() =>
    this.versions().find((v) => v.id === this.versionId()),
  );
  readonly loaded = signal(false);
  /** Goes up once a version was created, changed, published or archived:
   * whatever the sections loaded for it may be different now. */
  readonly revision = signal(0);

  reload(then?: () => void): void {
    this.api.getVersions().subscribe({
      next: (versions) => {
        this.versions.set(versions);
        if (!versions.some((v) => v.id === this.versionId())) {
          const today = timetableVersionOn(versions, toDateOnly(new Date()));
          this.versionId.set(today?.id ?? versions[0]?.id ?? null);
        }
        this.loaded.set(true);
        then?.();
      },
      error: (err) => {
        console.error('getVersions failed', err);
        this.loaded.set(true);
      },
    });
  }

  /** After a change of the versions themselves. */
  changed(): void {
    this.reload(() => this.revision.update((n) => n + 1));
  }
}
