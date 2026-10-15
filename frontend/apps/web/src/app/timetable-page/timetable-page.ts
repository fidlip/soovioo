import { Component, computed, inject, signal } from '@angular/core';
import { Card } from '@openng/optimus-ui/card';
import { Tag } from '@openng/optimus-ui/tag';
import { DayOfWeek, TimetableApiService, TimetableEntry } from '@sova/shared';

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

// Single demo class for now; a real class picker lands once auth/roles exist.
const DEMO_SCHOOL_CLASS_ID = 1;

@Component({
  selector: 'app-timetable-page',
  imports: [Card, Tag],
  templateUrl: './timetable-page.html',
})
export class TimetablePage {
  private readonly timetableApi = inject(TimetableApiService);

  protected readonly days = DAYS;
  protected readonly dayLabels = DAY_LABELS;
  protected readonly entries = signal<TimetableEntry[]>([]);

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

  constructor() {
    this.timetableApi.getTimetable(DEMO_SCHOOL_CLASS_ID).subscribe({
      next: (entries) => this.entries.set(entries),
      error: (err) => console.error('getTimetable failed', err),
    });
  }
}
