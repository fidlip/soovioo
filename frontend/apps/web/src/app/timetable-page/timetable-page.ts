import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DayOfWeek,
  SchoolPeriod,
  SchoolPeriodsApiService,
  Subject,
  SubjectsApiService,
  Teacher,
  TeachersApiService,
  TimetableApiService,
  TimetableEntry,
  WeekParity,
} from '@sova/shared';

const DAYS: DayOfWeek[] = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday'];
const DAY_LABELS: Record<DayOfWeek, string> = {
  Monday: 'Pondělí',
  Tuesday: 'Úterý',
  Wednesday: 'Středa',
  Thursday: 'Čtvrtek',
  Friday: 'Pátek',
  Saturday: 'Sobota',
  Sunday: 'Neděle',
};

const DAY_OPTIONS = DAYS.map((value) => ({ value, label: DAY_LABELS[value] }));

const WEEK_PARITY_OPTIONS: { label: string; value: WeekParity }[] = [
  { label: 'Každý týden', value: 'Every' },
  { label: 'Lichý týden', value: 'Odd' },
  { label: 'Sudý týden', value: 'Even' },
];

// Single demo class for now; a real class picker lands once auth/roles exist.
const DEMO_SCHOOL_CLASS_ID = 1;

/** "HH:mm:ss" (backend TimeOnly) -> "HH:mm" (<input type="time">). */
function toInputTime(value: string): string {
  return value.slice(0, 5);
}

/** "HH:mm" (<input type="time">) -> "HH:mm:ss" (backend TimeOnly). */
function toBackendTime(value: string): string {
  return value.length === 5 ? `${value}:00` : value;
}

@Component({
  selector: 'app-timetable-page',
  imports: [FormsModule, PrimeTemplate, Button, Card, InputNumber, InputText, Select, TableModule, Tag],
  templateUrl: './timetable-page.html',
})
export class TimetablePage {
  private readonly timetableApi = inject(TimetableApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly teachersApi = inject(TeachersApiService);
  private readonly schoolPeriodsApi = inject(SchoolPeriodsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly days = DAYS;
  protected readonly dayLabels = DAY_LABELS;
  protected readonly dayOptions = DAY_OPTIONS;
  protected readonly weekParityOptions = WEEK_PARITY_OPTIONS;
  protected readonly entries = signal<TimetableEntry[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly teachers = signal<Teacher[]>([]);
  protected readonly periods = signal<SchoolPeriod[]>([]);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly subjectOptions = computed(() =>
    this.subjects().map((s) => ({ id: s.id, label: s.abbreviation ? `${s.abbreviation} — ${s.name}` : s.name })),
  );

  protected readonly teacherOptions = computed(() =>
    this.teachers().map((t) => ({ id: t.id, label: t.username })),
  );

  protected readonly periodsByNumber = computed(() => {
    const map = new Map<number, SchoolPeriod>();
    for (const period of this.periods()) {
      map.set(period.periodNumber, period);
    }
    return map;
  });

  protected readonly entriesByDay = computed(() => {
    const grouped = new Map<DayOfWeek, TimetableEntry[]>();
    for (const day of this.days) {
      grouped.set(
        day,
        this.entries()
          .filter((e) => e.dayOfWeek === day)
          .sort((a, b) => a.periodNumber - b.periodNumber),
      );
    }
    return grouped;
  });

  protected newSubjectId: number | null = null;
  protected newDayOfWeek: DayOfWeek = 'Monday';
  protected newPeriodNumber = 1;
  protected newWeekParity: WeekParity = 'Every';
  protected newTeacherId: number | null = null;

  protected editingEntryId: number | null = null;
  protected editSubjectId: number | null = null;
  protected editDayOfWeek: DayOfWeek = 'Monday';
  protected editPeriodNumber = 1;
  protected editWeekParity: WeekParity = 'Every';
  protected editTeacherId: number | null = null;

  protected newPeriodNumberToConfigure = 1;
  protected newPeriodStartTime = '08:00';
  protected newPeriodEndTime = '08:45';

  protected editingPeriodNumber: number | null = null;
  protected editPeriodStartTime = '';
  protected editPeriodEndTime = '';

  constructor() {
    this.reload();
    this.reloadPeriods();
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.newSubjectId ??= subjects[0]?.id ?? null;
      },
      error: (err) => console.error('getSubjects failed', err),
    });
    this.teachersApi.getTeachers().subscribe({
      next: (teachers) => this.teachers.set(teachers),
      error: (err) => console.error('getTeachers failed', err),
    });
  }

  protected canManageTimetable(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected periodTimeLabel(periodNumber: number): string {
    const period = this.periodsByNumber().get(periodNumber);
    return period ? `${toInputTime(period.startTime)}–${toInputTime(period.endTime)}` : '—';
  }

  protected addEntry(): void {
    if (!this.newSubjectId) {
      return;
    }
    this.timetableApi
      .addEntry(DEMO_SCHOOL_CLASS_ID, {
        subjectId: this.newSubjectId,
        dayOfWeek: this.newDayOfWeek,
        periodNumber: this.newPeriodNumber,
        weekParity: this.newWeekParity,
        teacherId: this.newTeacherId,
      })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addEntry failed', err),
      });
  }

  protected startEditEntry(entry: TimetableEntry): void {
    this.editingEntryId = entry.id;
    this.editSubjectId = entry.subjectId;
    this.editDayOfWeek = entry.dayOfWeek;
    this.editPeriodNumber = entry.periodNumber;
    this.editWeekParity = entry.weekParity;
    this.editTeacherId = entry.teacherId;
  }

  protected cancelEditEntry(): void {
    this.editingEntryId = null;
  }

  protected saveEditEntry(): void {
    if (this.editingEntryId === null || !this.editSubjectId) {
      return;
    }
    this.timetableApi
      .updateEntry(DEMO_SCHOOL_CLASS_ID, this.editingEntryId, {
        subjectId: this.editSubjectId,
        dayOfWeek: this.editDayOfWeek,
        periodNumber: this.editPeriodNumber,
        weekParity: this.editWeekParity,
        teacherId: this.editTeacherId,
      })
      .subscribe({
        next: () => {
          this.editingEntryId = null;
          this.reload();
        },
        error: (err) => console.error('updateEntry failed', err),
      });
  }

  protected deleteEntry(entry: TimetableEntry): void {
    this.timetableApi.deleteEntry(DEMO_SCHOOL_CLASS_ID, entry.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteEntry failed', err),
    });
  }

  protected addPeriod(): void {
    this.schoolPeriodsApi
      .upsertPeriod(this.newPeriodNumberToConfigure, {
        startTime: toBackendTime(this.newPeriodStartTime),
        endTime: toBackendTime(this.newPeriodEndTime),
      })
      .subscribe({
        next: () => this.reloadPeriods(),
        error: (err) => console.error('upsertPeriod failed', err),
      });
  }

  protected startEditPeriod(period: SchoolPeriod): void {
    this.editingPeriodNumber = period.periodNumber;
    this.editPeriodStartTime = toInputTime(period.startTime);
    this.editPeriodEndTime = toInputTime(period.endTime);
  }

  protected cancelEditPeriod(): void {
    this.editingPeriodNumber = null;
  }

  protected saveEditPeriod(): void {
    if (this.editingPeriodNumber === null) {
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
        error: (err) => console.error('upsertPeriod failed', err),
      });
  }

  protected deletePeriod(period: SchoolPeriod): void {
    this.schoolPeriodsApi.deletePeriod(period.periodNumber).subscribe({
      next: () => this.reloadPeriods(),
      error: (err) => console.error('deletePeriod failed', err),
    });
  }

  private reload(): void {
    this.timetableApi.getTimetable(DEMO_SCHOOL_CLASS_ID).subscribe({
      next: (entries) => this.entries.set(entries),
      error: (err) => console.error('getTimetable failed', err),
    });
  }

  private reloadPeriods(): void {
    this.schoolPeriodsApi.getPeriods().subscribe({
      next: (periods) => this.periods.set(periods),
      error: (err) => console.error('getPeriods failed', err),
    });
  }
}
