import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import {
  SUPPORT_ACCESS_LABELS,
  SchoolSupport,
  SupportAccess,
  SupportApiService,
  approvalDuration,
} from '@soovioo/shared';
import { FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** Vstup podpory do školy in Údaje o škole: whether the support may enter
 * the school — only the school's admin changes it, the others see it. */
@Component({
  selector: 'app-support-access-settings',
  imports: [Button, Card, FloatField, FormsModule, InfoHint, Select],
  templateUrl: './support-access-settings.html',
})
export class SupportAccessSettings {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);

  protected readonly accessOptions = (
    Object.keys(SUPPORT_ACCESS_LABELS) as SupportAccess[]
  ).map((value) => ({ value, label: SUPPORT_ACCESS_LABELS[value] }));
  protected readonly support = signal<SchoolSupport | null>(null);
  protected readonly access = signal<SupportAccess>('Approval');
  protected readonly duration = computed(() =>
    approvalDuration(this.support()?.approvalMinutes ?? 24 * 60),
  );

  constructor() {
    this.api.getSchoolSupport().subscribe({
      next: (support) => this.apply(support),
      error: (err) => console.error('getSchoolSupport failed', err),
    });
  }

  protected save(): void {
    this.api.setSupportAccess(this.access()).subscribe({
      next: (support) => {
        this.apply(support);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setSupportAccess failed', err),
    });
  }

  private apply(support: SchoolSupport): void {
    this.support.set(support);
    this.access.set(support.access);
  }
}
