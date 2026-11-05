import { Injectable, computed, inject, signal } from '@angular/core';
import {
  TimetableVersion,
  TimetableVersionsApiService,
  timetableVersionOn,
} from '@soovioo/shared';
import { toDateOnly } from '../departures/retention-labels';
import { versionStatusLabel } from './timetable-versions-dialog';

/** Administrace → Výuka: the timetable version all its sections work with. */
@Injectable()
export class TimetableVersionSelection {
  private readonly api = inject(TimetableVersionsApiService);

  readonly versions = signal<TimetableVersion[]>([]);
  /** null while they aren't in, or for a school without a timetable. */
  readonly versionId = signal<number | null>(null);
  readonly loaded = signal(false);
  readonly options = computed(() =>
    this.versions().map((v) => ({
      label: `${v.name} (${versionStatusLabel(v, toDateOnly(new Date())).label.toLowerCase()})`,
      value: v.id,
    })),
  );

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
}
