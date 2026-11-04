import { Component, computed, input, model } from '@angular/core';
import { AvailabilityKind, AvailabilitySlot, DayOfWeek } from '@soovioo/shared';

const DAYS: { value: DayOfWeek; label: string; name: string }[] = [
  { value: 'Monday', label: 'Po', name: 'pondělí' },
  { value: 'Tuesday', label: 'Út', name: 'úterý' },
  { value: 'Wednesday', label: 'St', name: 'středa' },
  { value: 'Thursday', label: 'Čt', name: 'čtvrtek' },
  { value: 'Friday', label: 'Pá', name: 'pátek' },
];

const NEXT: Record<'Free' | AvailabilityKind, 'Free' | AvailabilityKind> = {
  Free: 'Avoid',
  Avoid: 'Unavailable',
  Unavailable: 'Free',
};

const LABEL: Record<'Free' | AvailabilityKind, string> = {
  Free: 'může',
  Avoid: 'raději ne',
  Unavailable: 'nemůže',
};

const STYLES: Record<'Free' | AvailabilityKind, string> = {
  Free: 'border-surface-200 text-green-600 dark:border-surface-700 dark:text-green-400',
  Avoid:
    'border-amber-400 bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-200',
  Unavailable:
    'border-red-400 bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300',
};

/** Den × hodina: může / raději ne / nemůže — a click moves a cell to the
 * next state, the day's button makes the whole day off (or not). */
@Component({
  selector: 'app-teacher-availability',
  templateUrl: './teacher-availability.html',
})
export class TeacherAvailability {
  readonly periods = input.required<number[]>();
  readonly value = model<AvailabilitySlot[]>([]);

  protected readonly days = DAYS;
  protected readonly label = LABEL;
  protected readonly styles = STYLES;
  protected readonly daysOff = computed(
    () =>
      new Set(
        this.value()
          .filter((s) => s.period === null && s.kind === 'Unavailable')
          .map((s) => s.day),
      ),
  );

  protected state(day: DayOfWeek, period: number): 'Free' | AvailabilityKind {
    if (this.daysOff().has(day)) {
      return 'Unavailable';
    }
    return (
      this.value().find((s) => s.day === day && s.period === period)?.kind ??
      'Free'
    );
  }

  protected cycle(day: DayOfWeek, period: number): void {
    const next = NEXT[this.state(day, period)];
    const rest = this.value().filter(
      (s) => !(s.day === day && s.period === period),
    );
    this.value.set(
      next === 'Free' ? rest : [...rest, { day, period, kind: next }],
    );
  }

  protected toggleDay(day: DayOfWeek): void {
    const rest = this.value().filter((s) => s.day !== day);
    this.value.set(
      this.daysOff().has(day)
        ? rest
        : [...rest, { day, period: null, kind: 'Unavailable' }],
    );
  }
}
