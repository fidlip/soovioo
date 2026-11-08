import {
  Component,
  computed,
  inject,
  input,
  model,
  signal,
} from '@angular/core';
import {
  DayOfWeek,
  SchoolPeriod,
  SchoolPeriodsApiService,
  Student,
  TimetableApiService,
  TimetableEntry,
  weekParityOf,
} from '@soovioo/shared';

const DAYS_BY_INDEX: DayOfWeek[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

export interface DayPeriod {
  number: number;
  /** The pupil's lesson then, null = none. */
  lesson: string | null;
}

/** Local (not UTC) y-m-d — `toISOString().slice(0, 10)` converts through
 * UTC first, which rolls local midnight back to the previous day in any
 * timezone ahead of UTC (e.g. CET/CEST). */
export function toDateOnlyString(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** The school's periods for a pupil's day, each with the lesson the pupil
 * has then (their class's and their groups', by the version valid that
 * day, incl. odd/even weeks) — the chips to pick missed periods from.
 * Timetables are cached per pupil and date. Create in an injection
 * context. */
export class StudentDayPeriods {
  private readonly timetableApi = inject(TimetableApiService);
  private readonly schoolPeriods = signal<SchoolPeriod[]>([]);
  private readonly timetables = signal<Record<string, TimetableEntry[]>>({});
  private readonly studentId = signal<number | null>(null);
  private readonly date = signal(new Date());

  readonly periods = computed<DayPeriod[]>(() => {
    const studentId = this.studentId();
    const date = this.date();
    const entries =
      studentId !== null
        ? (this.timetables()[this.key(studentId, date)] ?? [])
        : [];
    const day = DAYS_BY_INDEX[date.getDay()];
    const parity = weekParityOf(date);
    return this.schoolPeriods().map((period) => {
      const entry = entries.find(
        (e) =>
          e.dayOfWeek === day &&
          e.periodNumber === period.periodNumber &&
          (e.weekParity === 'Every' || e.weekParity === parity),
      );
      return {
        number: period.periodNumber,
        lesson: entry ? (entry.subjectAbbreviation ?? entry.subjectName) : null,
      };
    });
  });

  constructor() {
    inject(SchoolPeriodsApiService)
      .getPeriods()
      .subscribe({
        next: (periods) => this.schoolPeriods.set(periods),
        error: (err) => console.error('getPeriods failed', err),
      });
  }

  /** The periods with a lesson. */
  lessonPeriods(): number[] {
    return this.periods()
      .filter((p) => p.lesson !== null)
      .map((p) => p.number);
  }

  /** Shows the pupil's day; `loaded` runs once its timetable is there. */
  show(student: Student | undefined, date: Date, loaded?: () => void): void {
    this.studentId.set(student?.id ?? null);
    this.date.set(date);
    if (!student) {
      return;
    }
    const key = this.key(student.id, date);
    if (this.timetables()[key]) {
      loaded?.();
      return;
    }
    this.timetableApi
      .getTimetable(student.schoolClassId, {
        date: toDateOnlyString(date),
        studentId: student.id,
      })
      .subscribe({
        next: (entries) => {
          this.timetables.update((m) => ({ ...m, [key]: entries }));
          loaded?.();
        },
        error: (err) => console.error('getTimetable failed', err),
      });
  }

  private key(studentId: number, date: Date): string {
    return `${studentId}|${toDateOnlyString(date)}`;
  }
}

/** Chips of a day's periods with the pupil's lessons, to pick missed ones. */
@Component({
  selector: 'app-period-chips',
  template: `
    <div
      class="flex flex-wrap gap-1.5"
      role="group"
      [attr.aria-label]="label()"
    >
      @for (period of periods(); track period.number) {
        <button
          type="button"
          class="flex min-w-12 flex-col items-center rounded-border border px-2 py-1 text-sm transition-colors"
          [class.border-primary]="isSelected(period.number)"
          [class.bg-primary]="isSelected(period.number)"
          [class.text-primary-contrast]="isSelected(period.number)"
          [class.border-surface-300]="!isSelected(period.number)"
          [class.text-surface-500]="
            !isSelected(period.number) && !period.lesson
          "
          [attr.aria-pressed]="isSelected(period.number)"
          (click)="toggle(period.number)"
        >
          <span class="font-semibold">{{ period.number }}.</span>
          <span class="text-xs">{{ period.lesson ?? '—' }}</span>
        </button>
      }
    </div>
  `,
})
export class PeriodChips {
  readonly periods = input.required<DayPeriod[]>();
  readonly label = input('Zameškané hodiny');
  readonly selected = model<number[]>([]);

  protected isSelected(number: number): boolean {
    return this.selected().includes(number);
  }

  protected toggle(number: number): void {
    this.selected.update((periods) =>
      periods.includes(number)
        ? periods.filter((p) => p !== number)
        : [...periods, number].sort((a, b) => a - b),
    );
  }
}
