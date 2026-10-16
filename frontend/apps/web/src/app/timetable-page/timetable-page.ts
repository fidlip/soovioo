import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DayOfWeek,
  Subject,
  SubjectsApiService,
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

@Component({
  selector: 'app-timetable-page',
  imports: [FormsModule, Button, Card, InputNumber, Select, Tag],
  templateUrl: './timetable-page.html',
})
export class TimetablePage {
  private readonly timetableApi = inject(TimetableApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly days = DAYS;
  protected readonly dayLabels = DAY_LABELS;
  protected readonly dayOptions = DAY_OPTIONS;
  protected readonly weekParityOptions = WEEK_PARITY_OPTIONS;
  protected readonly entries = signal<TimetableEntry[]>([]);
  protected readonly subjects = signal<Subject[]>([]);

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

  protected editingEntryId: number | null = null;
  protected editSubjectId: number | null = null;
  protected editDayOfWeek: DayOfWeek = 'Monday';
  protected editPeriodNumber = 1;
  protected editWeekParity: WeekParity = 'Every';

  constructor() {
    this.reload();
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.newSubjectId ??= subjects[0]?.id ?? null;
      },
      error: (err) => console.error('getSubjects failed', err),
    });
  }

  protected canManageTimetable(): boolean {
    return this.authApi.isTeacherOrAdmin();
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

  private reload(): void {
    this.timetableApi.getTimetable(DEMO_SCHOOL_CLASS_ID).subscribe({
      next: (entries) => this.entries.set(entries),
      error: (err) => console.error('getTimetable failed', err),
    });
  }
}
