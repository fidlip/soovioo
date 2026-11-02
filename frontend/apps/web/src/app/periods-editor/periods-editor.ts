import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { InputText } from '@openng/optimus-ui/inputtext';
import { SchoolPeriod, SchoolPeriodsApiService } from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** Bell schedule (start/end of each period, incl. a "zero" period) —
 * school-wide settings, so it lives in Administrace, not in the timetable. */
@Component({
  selector: 'app-periods-editor',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    DatePicker,
    InputText,
    InfoHint,
  ],
  templateUrl: './periods-editor.html',
})
export class PeriodsEditor {
  private readonly schoolPeriodsApi = inject(SchoolPeriodsApiService);
  private readonly notify = inject(NotifyService);

  protected readonly periods = signal<SchoolPeriod[]>([]);

  protected newPeriodNumberToConfigure = 1;
  protected newPeriodStartTime = timeStringToDate('08:00');
  protected newPeriodEndTime = timeStringToDate('08:45');
  protected editingPeriodNumber: number | null = null;
  protected editPeriodStartTime: Date | null = null;
  protected editPeriodEndTime: Date | null = null;

  constructor() {
    this.reloadPeriods();
  }

  protected addPeriod(): void {
    this.schoolPeriodsApi
      .upsertPeriod(this.newPeriodNumberToConfigure, {
        startTime: toBackendTime(this.newPeriodStartTime),
        endTime: toBackendTime(this.newPeriodEndTime),
      })
      .subscribe({
        next: () => {
          this.newPeriodNumberToConfigure += 1;
          this.reloadPeriods();
        },
        error: (err) => this.notify.actionFailed('upsertPeriod failed', err),
      });
  }

  protected startEditPeriod(period: SchoolPeriod): void {
    this.editingPeriodNumber = period.periodNumber;
    this.editPeriodStartTime = timeStringToDate(period.startTime.slice(0, 5));
    this.editPeriodEndTime = timeStringToDate(period.endTime.slice(0, 5));
  }

  protected cancelEditPeriod(): void {
    this.editingPeriodNumber = null;
  }

  protected saveEditPeriod(): void {
    if (
      this.editingPeriodNumber === null ||
      !this.editPeriodStartTime ||
      !this.editPeriodEndTime
    ) {
      return;
    }
    this.schoolPeriodsApi
      .upsertPeriod(this.editingPeriodNumber, {
        startTime: toBackendTime(this.editPeriodStartTime),
        endTime: toBackendTime(this.editPeriodEndTime),
      })
      .subscribe({
        next: () => {
          this.editingPeriodNumber = null;
          this.reloadPeriods();
        },
        error: (err) => this.notify.actionFailed('upsertPeriod failed', err),
      });
  }

  protected deletePeriod(period: SchoolPeriod): void {
    this.schoolPeriodsApi.deletePeriod(period.periodNumber).subscribe({
      next: () => this.reloadPeriods(),
      error: (err) => this.notify.actionFailed('deletePeriod failed', err),
    });
  }

  private reloadPeriods(): void {
    this.schoolPeriodsApi.getPeriods().subscribe({
      next: (periods) => {
        this.periods.set(periods);
        const next = Math.max(0, ...periods.map((p) => p.periodNumber + 1));
        this.newPeriodNumberToConfigure = Math.min(next, 15);
      },
      error: (err) => console.error('getPeriods failed', err),
    });
  }
}

/** p-datePicker's ngModel needs a Date even in timeOnly mode — only the
 * hours/minutes are read back out. */
function timeStringToDate(hhmm: string): Date {
  const [hours, minutes] = hhmm.split(':').map(Number);
  const date = new Date();
  date.setHours(hours, minutes, 0, 0);
  return date;
}

/** Date -> "HH:mm:00" (backend TimeOnly). */
function toBackendTime(date: Date): string {
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');
  return `${hours}:${minutes}:00`;
}
