import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed, toObservable } from '@angular/core/rxjs-interop';
import { filter, fromEvent, interval, merge } from 'rxjs';
import { LiveService } from './live.service';

/** `version.json` as the CI writes it into the web image
 * (.github/scripts/version-json.sh). Outside a deployed build it's an empty
 * placeholder. */
export interface DeployedVersionInfo {
  version?: string;
  commit?: string;
  builtAt?: string;
  repository?: string;
  pullRequests?: { number: number; title: string; mergedAt: string }[];
}

const CHECK_EVERY_MS = 5 * 60_000;

/** The deployed version this tab runs, and whether a newer one has been
 * deployed since it loaded — then its requests may no longer fit the API.
 * Checked when the tab comes back to the front, when the live connection
 * reconnects (the API restarts on a deploy) and every few minutes. */
@Injectable({ providedIn: 'root' })
export class AppVersionService {
  private readonly http = inject(HttpClient);

  /** What the tab loaded with; null outside a deployed build. */
  readonly info = signal<DeployedVersionInfo | null>(null);
  readonly outdated = signal(false);

  constructor() {
    this.fetch((info) => this.info.set(info));
    merge(
      fromEvent(document, 'visibilitychange'),
      interval(CHECK_EVERY_MS),
      toObservable(inject(LiveService).connected).pipe(filter(Boolean)),
    )
      .pipe(
        filter(() => document.visibilityState === 'visible'),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.check());
  }

  private check(): void {
    const loaded = this.info()?.commit;
    if (!loaded || this.outdated()) {
      return;
    }
    this.fetch((info) => {
      if (info?.commit && info.commit !== loaded) {
        this.outdated.set(true);
      }
    });
  }

  private fetch(then: (info: DeployedVersionInfo | null) => void): void {
    this.http.get<DeployedVersionInfo>('/version.json').subscribe({
      next: (info) => then(info?.version ? info : null),
      error: () => undefined,
    });
  }
}
