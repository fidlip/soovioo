import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import { MySchoolApiService, ReportCardMarkFormat } from '@soovioo/shared';
import { FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** Vysvědčení in Údaje o škole: whether a stupeň goes on the report card as
 * "výborný" or "1" — the school sets it in its školní řád. */
@Component({
  selector: 'app-report-card-settings',
  imports: [Button, Card, FloatField, FormsModule, InfoHint, Select],
  templateUrl: './report-card-settings.html',
})
export class ReportCardSettings {
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly notify = inject(NotifyService);

  protected readonly markFormatOptions: {
    value: ReportCardMarkFormat;
    label: string;
  }[] = [
    { value: 'Words', label: 'Slovem (výborný)' },
    { value: 'Digits', label: 'Číslem (1)' },
  ];
  protected readonly markFormat = signal<ReportCardMarkFormat>('Words');

  constructor() {
    this.mySchoolApi.getReportCardSettings().subscribe({
      next: (settings) => this.markFormat.set(settings.markFormat),
      error: (err) => console.error('getReportCardSettings failed', err),
    });
  }

  protected save(): void {
    this.mySchoolApi
      .setReportCardSettings({ markFormat: this.markFormat() })
      .subscribe({
        next: (settings) => {
          this.markFormat.set(settings.markFormat);
          this.notify.success('Uloženo.');
        },
        error: (err) =>
          this.notify.actionFailed('setReportCardSettings failed', err),
      });
  }
}
