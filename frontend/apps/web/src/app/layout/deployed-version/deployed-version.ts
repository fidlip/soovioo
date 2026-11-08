import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { Popover } from '@openng/optimus-ui/popover';
import { DateFormatPipe } from '@soovioo/shared';

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

/** The deployed version at the bottom of the sidebar; a click shows the
 * pull requests it's made of, with links to GitHub. */
@Component({
  selector: 'app-deployed-version',
  imports: [DateFormatPipe, Popover],
  templateUrl: './deployed-version.html',
  styleUrl: './deployed-version.scss',
})
export class DeployedVersion {
  protected readonly info = signal<DeployedVersionInfo | null>(null);

  constructor() {
    inject(HttpClient)
      .get<DeployedVersionInfo>('/version.json')
      .subscribe({
        next: (info) => this.info.set(info?.version ? info : null),
        error: () => this.info.set(null),
      });
  }
}
