import { Component, NO_ERRORS_SCHEMA, computed, inject, signal } from '@angular/core';
import { NativeScriptCommonModule } from '@nativescript/angular';
import { DayOfWeek, TimetableApiService, TimetableEntry } from '@sova/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

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

// Single demo class for now, same as web.
const DEMO_SCHOOL_CLASS_ID = 1;

interface DaySection {
  day: DayOfWeek;
  label: string;
  entries: TimetableEntry[];
}

@Component({
  selector: 'ns-timetable',
  templateUrl: './timetable.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class TimetableComponent {
  private readonly timetableApi = inject(TimetableApiService);

  private readonly entries = signal<TimetableEntry[]>([]);

  protected readonly sections = computed<DaySection[]>(() =>
    DAYS.map((day) => ({
      day,
      label: DAY_LABELS[day],
      entries: this.entries()
        .filter((e) => e.dayOfWeek === day)
        .sort((a, b) => a.periodNumber - b.periodNumber),
    })),
  );

  constructor() {
    this.timetableApi.getTimetable(DEMO_SCHOOL_CLASS_ID).subscribe({
      next: (entries) => this.entries.set(entries),
      error: (err) => console.error('getTimetable failed', err),
    });
  }
}
