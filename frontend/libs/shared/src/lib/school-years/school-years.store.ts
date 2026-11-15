import { Injectable, computed, inject, signal } from '@angular/core';
import {
  DayOffSpan,
  ReportPeriodOption,
  SchoolYear,
  dayOffOn,
  dayOffSpans,
} from './school-year.model';
import { SchoolYearsApiService } from './school-years-api.service';

/** The school's years, loaded once and kept for the session. */
@Injectable({ providedIn: 'root' })
export class SchoolYearsStore {
  private readonly api = inject(SchoolYearsApiService);
  private loading = false;
  /** Bumped by {@link clear}: a response asked for before it is dropped. */
  private session = 0;

  /** Null until loaded. */
  readonly years = signal<SchoolYear[] | null>(null);

  /** The year today is in; null without one (or before the load). */
  readonly current = computed(
    () => this.years()?.find((y) => y.current) ?? null,
  );

  /** Loaded, and today has no school year: nothing can be recorded. */
  readonly missing = computed(
    () => this.years() !== null && this.current() === null,
  );

  /** Periods that end with a report card, newest first. */
  readonly reportPeriods = computed<ReportPeriodOption[]>(() =>
    (this.years() ?? [])
      .flatMap((year) =>
        year.periods
          .filter((p) => p.report === 'ReportCard')
          .map((period) => ({
            id: period.id,
            label: `${year.name} – ${period.name}`,
            period,
          })),
      )
      .reverse(),
  );

  /** The days without lessons, the hlavní prázdniny included. */
  readonly daysOff = computed(() => dayOffSpans(this.years() ?? []));

  /** Why `date` (YYYY-MM-DD) is off for the class (without one: for the
   * whole school); null on a day of lessons. Weekends aside. */
  dayOff(date: string, classId?: number | null): DayOffSpan | null {
    return dayOffOn(this.daysOff(), date, classId);
  }

  /** Loads the years unless they are here (or on their way). */
  ensure(): void {
    if (this.years() === null && !this.loading) {
      this.reload();
    }
  }

  /** Loads the years again; the ones here stay until they come. */
  reload(): void {
    const session = this.session;
    this.loading = true;
    this.api.getSchoolYears().subscribe({
      next: (years) => {
        if (session === this.session) {
          this.loading = false;
          this.years.set(years);
        }
      },
      error: (err) => {
        if (session === this.session) {
          this.loading = false;
        }
        console.error('getSchoolYears failed', err);
      },
    });
  }

  /** After signing out or switching the account; a load still on its way
   * is for the previous one. */
  clear(): void {
    this.session++;
    this.loading = false;
    this.years.set(null);
  }

  /** The report card period a day is in; without one, the latest that has
   * started; else the newest. */
  reportPeriodOn(day: string): ReportPeriodOption | null {
    const options = this.reportPeriods();
    return (
      options.find((o) => o.period.from <= day && day <= o.period.to) ??
      options.find((o) => o.period.from <= day) ??
      options[0] ??
      null
    );
  }
}
