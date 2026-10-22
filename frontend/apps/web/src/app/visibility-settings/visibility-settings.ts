import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import {
  MySchoolApiService,
  VisibilityOption,
  VisibilitySettings as VisibilitySettingsModel,
} from '@soovioo/shared';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import { VISIBILITY_OPTIONS } from '../visibility-options';

/** Administrace → Viditelnost údajů: what the school shares with parents
 * and pupils, and where individuals may hide their own data. The API
 * enforces it — what isn't shared never leaves the backend. */
@Component({
  selector: 'app-visibility-settings',
  imports: [
    FormsModule,
    Button,
    Card,
    Checkbox,
    ToggleSwitch,
    InfoHint,
    TermPipe,
  ],
  templateUrl: './visibility-settings.html',
})
export class VisibilitySettings {
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly notify = inject(NotifyService);

  protected readonly options = VISIBILITY_OPTIONS;
  /** The whole loaded state — options not shown here (photos) are kept
   * as they are on save. */
  protected readonly settings = signal<VisibilitySettingsModel | null>(null);

  constructor() {
    this.mySchoolApi.getVisibility().subscribe({
      next: (settings) => this.settings.set(settings),
      error: (err) => this.notify.actionFailed('getVisibility failed', err),
    });
  }

  protected isShared(option: VisibilityOption): boolean {
    return this.settings()?.shared.includes(option) ?? false;
  }

  protected isOptOutAllowed(option: VisibilityOption): boolean {
    return this.settings()?.optOutAllowed.includes(option) ?? false;
  }

  protected setShared(option: VisibilityOption, on: boolean): void {
    this.update('shared', option, on);
  }

  protected setOptOutAllowed(option: VisibilityOption, on: boolean): void {
    this.update('optOutAllowed', option, on);
  }

  protected save(): void {
    const settings = this.settings();
    if (!settings) {
      return;
    }
    this.mySchoolApi.setVisibility(settings).subscribe({
      next: (saved) => {
        this.settings.set(saved);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setVisibility failed', err),
    });
  }

  private update(
    key: keyof VisibilitySettingsModel,
    option: VisibilityOption,
    on: boolean,
  ): void {
    const settings = this.settings();
    if (!settings) {
      return;
    }
    const rest = settings[key].filter((o) => o !== option);
    this.settings.set({ ...settings, [key]: on ? [...rest, option] : rest });
  }
}
