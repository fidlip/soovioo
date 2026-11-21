import { Component, computed, input } from '@angular/core';
import { ClassBookLesson, LessonAttendanceStatus } from '@soovioo/shared';

type BarState = LessonAttendanceStatus | 'Present';

const ORDER: { state: BarState; label: string }[] = [
  { state: 'Present', label: 'přítomno' },
  { state: 'Late', label: 'pozdě' },
  { state: 'LeftEarly', label: 'odchod' },
  { state: 'Away', label: 'mimo třídu' },
  { state: 'Absent', label: 'chybí' },
];

/** A segment per pupil in their state's colour, present first; grey while
 * the attendance isn't taken. */
export function barSegments(lesson: ClassBookLesson): BarState[] | null {
  if (!lesson.attendanceTaken) {
    return null;
  }
  const count = (state: BarState) =>
    state === 'Present'
      ? Math.max(0, lesson.pupilCount - lesson.marks.length)
      : lesson.marks.filter((m) => m.status === state).length;
  return ORDER.flatMap(({ state }) => Array(count(state)).fill(state));
}

/** "přítomno 24, pozdě 1, chybí 2". */
export function barLabel(lesson: ClassBookLesson): string {
  const segments = barSegments(lesson);
  if (!segments) {
    return 'Docházka nezapsána';
  }
  return ORDER.map(({ state, label }) => ({
    label,
    count: segments.filter((s) => s === state).length,
  }))
    .filter((p) => p.count > 0)
    .map((p) => `${p.label} ${p.count}`)
    .join(', ');
}

/** The lesson's attendance as a bar, a ratio without names. */
@Component({
  selector: 'app-attendance-bar',
  templateUrl: './attendance-bar.html',
  styleUrl: './attendance-bar.scss',
})
export class AttendanceBar {
  readonly lesson = input.required<ClassBookLesson>();

  protected readonly segments = computed(() => barSegments(this.lesson()));
  protected readonly label = computed(() => barLabel(this.lesson()));
}
