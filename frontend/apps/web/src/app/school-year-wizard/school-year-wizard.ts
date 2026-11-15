import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import {
  DateFormatPipe,
  SchoolYear,
  SchoolYearDates,
  SchoolYearsApiService,
  SchoolYearsStore,
} from '@soovioo/shared';
import { toDateOnlyString } from '../absences-page/student-day-periods';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';

function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** Founds the school's next year (Termíny → Souhrn), or changes the dates
 * of one it has. */
@Component({
  selector: 'app-school-year-wizard',
  imports: [
    Button,
    DateFormatPipe,
    DatePicker,
    Dialog,
    ...FORM_FIELDS,
    FloatField,
    FormsModule,
    Loading,
  ],
  templateUrl: './school-year-wizard.html',
})
export class SchoolYearWizard {
  private readonly api = inject(SchoolYearsApiService);
  private readonly store = inject(SchoolYearsStore);
  private readonly notify = inject(NotifyService);

  protected readonly visible = signal(false);
  protected readonly step = signal<'dates' | 'summary'>('dates');
  protected readonly saving = signal(false);
  /** The year being changed; null when founding one. */
  protected readonly editing = signal<SchoolYear | null>(null);
  /** The year being founded; null until its dates arrive. */
  protected readonly founding = signal<{
    startYear: number;
    name: string;
    official: boolean;
  } | null>(null);

  protected teachingStart: Date | null = null;
  protected firstHalfIssuedOn: Date | null = null;
  protected secondHalfIssuedOn: Date | null = null;

  /** Founds the next year. */
  open(): void {
    this.editing.set(null);
    this.founding.set(null);
    this.step.set('dates');
    this.notify.clear();
    this.visible.set(true);
    this.api.getSuggestion().subscribe({
      next: (suggestion) => {
        this.founding.set(suggestion);
        this.fill(suggestion);
      },
      error: (err) => {
        this.notify.actionFailed('getSuggestion failed', err);
        this.visible.set(false);
      },
    });
  }

  /** Changes the dates of a year. */
  edit(year: SchoolYear): void {
    const [first, second] = year.periods.filter(
      (p) => p.report === 'ReportCard',
    );
    this.editing.set(year);
    this.founding.set(null);
    this.step.set('dates');
    this.fill({
      teachingStart: year.teachingStart,
      firstHalfIssuedOn: first?.issuedOn ?? year.teachingStart,
      secondHalfIssuedOn: second?.issuedOn ?? year.to,
    });
    this.notify.clear();
    this.visible.set(true);
  }

  protected title(): string {
    const editing = this.editing();
    return editing
      ? `Termíny školního roku ${editing.name}`
      : `Založení školního roku ${this.founding()?.name ?? ''}`;
  }

  protected next(): void {
    if (this.editing()) {
      this.save();
    } else {
      this.step.set('summary');
    }
  }

  protected save(): void {
    const dates = this.dates();
    const founding = this.founding();
    const editing = this.editing();
    if (!dates || (!founding && !editing)) {
      return;
    }
    this.saving.set(true);
    const request = editing
      ? this.api.update(editing.id, dates)
      : this.api.create({ startYear: founding!.startYear, ...dates });
    request.subscribe({
      next: (year) => {
        this.saving.set(false);
        this.visible.set(false);
        this.store.reload();
        this.notify.success(
          editing
            ? `Termíny školního roku ${year.name} jsou uložené.`
            : `Školní rok ${year.name} je založený.`,
        );
      },
      error: (err) => {
        this.saving.set(false);
        this.step.set('dates');
        this.notify.actionFailed('saving the school year failed', err);
      },
    });
  }

  private fill(dates: SchoolYearDates): void {
    this.teachingStart = parseDateOnly(dates.teachingStart);
    this.firstHalfIssuedOn = parseDateOnly(dates.firstHalfIssuedOn);
    this.secondHalfIssuedOn = parseDateOnly(dates.secondHalfIssuedOn);
  }

  private dates(): SchoolYearDates | null {
    if (
      !this.teachingStart ||
      !this.firstHalfIssuedOn ||
      !this.secondHalfIssuedOn
    ) {
      return null;
    }
    return {
      teachingStart: toDateOnlyString(this.teachingStart),
      firstHalfIssuedOn: toDateOnlyString(this.firstHalfIssuedOn),
      secondHalfIssuedOn: toDateOnlyString(this.secondHalfIssuedOn),
    };
  }
}
