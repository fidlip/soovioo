import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { Step, StepList, Stepper } from '@openng/optimus-ui/stepper';
import {
  DateFormatPipe,
  PeriodReport,
  SchoolYear,
  SchoolYearRequest,
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

function addDays(date: Date, days: number): Date {
  return new Date(date.getFullYear(), date.getMonth(), date.getDate() + days);
}

/** A period as edited: it ends on `to`, the last one with the year. */
interface PeriodRow {
  /** Names its controls, so they stay with the row when others come and
   * go. */
  key: number;
  id: number | null;
  name: string;
  to: Date | null;
  report: PeriodReport;
  issuedOn: Date | null;
}

type WizardStep = 1 | 2;

/** The fields of the first step, so a server error on one of them shows
 * there. */
const YEAR_FIELDS = ['from', 'to', 'teachingStart', 'teachingEnd'];

/** Founds a school year, or changes one: its days (Školní rok), then its
 * periods (Období), freely divided as long as they follow one another. A
 * new year comes prefilled with MŠMT's dates and two pololetí. */
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
    InputText,
    Loading,
    Select,
    Step,
    StepList,
    Stepper,
  ],
  templateUrl: './school-year-wizard.html',
})
export class SchoolYearWizard {
  private readonly api = inject(SchoolYearsApiService);
  private readonly store = inject(SchoolYearsStore);
  private readonly notify = inject(NotifyService);

  protected readonly reportOptions: { value: PeriodReport; label: string }[] = [
    { value: 'None', label: 'Bez hodnocení' },
    { value: 'Interim', label: 'Průběžné hodnocení' },
    { value: 'ReportCard', label: 'Vysvědčení' },
  ];

  protected readonly visible = signal(false);
  protected readonly step = signal<WizardStep>(1);
  protected readonly saving = signal(false);
  protected readonly loaded = signal(false);
  /** The year being changed; null when founding one. */
  protected readonly editing = signal<SchoolYear | null>(null);
  /** Founding: the dates are MŠMT's; otherwise an estimate. */
  protected readonly official = signal(true);

  protected from: Date | null = null;
  protected to: Date | null = null;
  protected teachingStart: Date | null = null;
  protected teachingEnd: Date | null = null;
  protected periods: PeriodRow[] = [];
  private nextKey = 0;

  /** Founds the next year. */
  open(): void {
    this.start(null);
    this.api.getSuggestion().subscribe({
      next: (suggestion) => {
        this.official.set(suggestion.official);
        this.fill(suggestion);
      },
      error: (err) => {
        this.notify.actionFailed('getSuggestion failed', err);
        this.visible.set(false);
      },
    });
  }

  /** Changes a year's days and periods. */
  edit(year: SchoolYear): void {
    this.start(year);
    this.fill({
      ...year,
      periods: year.periods.map((p) => ({
        id: p.id,
        name: p.name,
        to: p.to,
        report: p.report,
        issuedOn: p.issuedOn,
      })),
    });
  }

  protected title(): string {
    const editing = this.editing();
    return editing
      ? `Školní rok ${editing.name}`
      : `Založení školního roku ${this.yearName()}`;
  }

  /** "2026/27", from the days as they stand. */
  protected yearName(): string {
    if (!this.from || !this.to) {
      return '';
    }
    const from = this.from.getFullYear();
    const to = this.to.getFullYear();
    return from === to
      ? `${from}`
      : `${from}/${String(to % 100).padStart(2, '0')}`;
  }

  /** The first day of the period at `index`. */
  protected periodFrom(index: number): Date | null {
    if (index === 0) {
      return this.from;
    }
    const previous = this.periods[index - 1].to;
    return previous ? addDays(previous, 1) : null;
  }

  protected isLast(index: number): boolean {
    return index === this.periods.length - 1;
  }

  /** A new period before the last one, ending halfway through it. */
  protected addPeriod(): void {
    const lastIndex = this.periods.length - 1;
    const start = this.periodFrom(lastIndex);
    const to =
      start && this.to
        ? addDays(
            start,
            Math.floor((this.to.getTime() - start.getTime()) / 86_400_000 / 2),
          )
        : null;
    this.periods = [
      ...this.periods.slice(0, lastIndex),
      {
        key: this.nextKey++,
        id: null,
        name: '',
        to,
        report: 'None',
        issuedOn: null,
      },
      this.periods[lastIndex],
    ];
  }

  /** Its days go to the next period; the last one's to the one before. */
  protected removePeriod(index: number): void {
    this.periods = this.periods.filter((_, i) => i !== index);
  }

  protected save(): void {
    const request = this.request();
    if (!request) {
      return;
    }
    const editing = this.editing();
    this.saving.set(true);
    (editing
      ? this.api.update(editing.id, request)
      : this.api.create(request)
    ).subscribe({
      next: (year) => {
        this.saving.set(false);
        this.visible.set(false);
        this.store.reload();
        this.notify.success(
          editing
            ? `Školní rok ${year.name} je uložený.`
            : `Školní rok ${year.name} je založený.`,
        );
      },
      error: (err) => {
        this.saving.set(false);
        const problem = (
          err as { error?: { errors?: Record<string, string[]> } }
        )?.error;
        if (problem?.errors) {
          problem.errors = this.byRowKey(problem.errors);
          if (
            Object.keys(problem.errors).some((f) => YEAR_FIELDS.includes(f))
          ) {
            this.step.set(1);
          }
        }
        this.notify.actionFailed('saving the school year failed', err);
      },
    });
  }

  /** The server names a period by its place (`periods[1].to`), the form
   * by its row. */
  private byRowKey(errors: Record<string, string[]>): Record<string, string[]> {
    return Object.fromEntries(
      Object.entries(errors).map(([field, messages]) => [
        field.replace(
          /^periods\[(\d+)\]/,
          (whole, index: string) =>
            `periods[${this.periods[Number(index)]?.key ?? index}]`,
        ),
        messages,
      ]),
    );
  }

  private start(year: SchoolYear | null): void {
    this.editing.set(year);
    this.loaded.set(false);
    this.official.set(true);
    this.step.set(1);
    this.notify.clear();
    this.visible.set(true);
  }

  private fill(request: SchoolYearRequest): void {
    this.from = parseDateOnly(request.from);
    this.to = parseDateOnly(request.to);
    this.teachingStart = parseDateOnly(request.teachingStart);
    this.teachingEnd = parseDateOnly(request.teachingEnd);
    this.periods = request.periods.map((p) => ({
      key: this.nextKey++,
      id: p.id,
      name: p.name,
      to: parseDateOnly(p.to),
      report: p.report,
      issuedOn: p.issuedOn ? parseDateOnly(p.issuedOn) : null,
    }));
    this.loaded.set(true);
  }

  private request(): SchoolYearRequest | null {
    if (!this.from || !this.to || !this.teachingStart || !this.teachingEnd) {
      return null;
    }
    const yearTo = this.to;
    return {
      from: toDateOnlyString(this.from),
      to: toDateOnlyString(yearTo),
      teachingStart: toDateOnlyString(this.teachingStart),
      teachingEnd: toDateOnlyString(this.teachingEnd),
      periods: this.periods.map((p, i) => {
        const to = this.isLast(i) ? yearTo : p.to;
        return {
          id: p.id,
          name: p.name,
          to: to ? toDateOnlyString(to) : '',
          report: p.report,
          issuedOn:
            p.report !== 'None' && p.issuedOn
              ? toDateOnlyString(p.issuedOn)
              : null,
        };
      }),
    };
  }
}
