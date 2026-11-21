import { Component, input } from '@angular/core';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { DayBacking, PupilLesson, PupilLessonState } from '@soovioo/shared';

const STATE_LABELS: Record<PupilLessonState, string> = {
  NotRecorded: 'nezapsáno',
  Present: 'přítomen',
  Absent: 'chyběl(a)',
  Late: 'pozdě',
  LeftEarly: 'odchod',
  Away: 'mimo třídu',
};

/** The lessons not yet written of a pupil's day. */
export function notRecorded(lessons: PupilLesson[] | null | undefined): number {
  return (lessons ?? []).filter((l) => l.state === 'NotRecorded').length;
}

export const APPROVAL_BLOCKED =
  'Schválit jde až po zapsání všech hodin v třídní knize.';

/** Approves the days — after asking, when some of their lessons aren't
 * written in the třídní kniha yet. */
export function approveBacked(
  confirmation: ConfirmationService,
  days: DayBacking[],
  approve: () => void,
): void {
  const missing = days.reduce((sum, d) => sum + notRecorded(d.lessons), 0);
  if (missing === 0) {
    approve();
    return;
  }
  confirmation.confirm({
    header: 'Nezapsané hodiny',
    message: `V třídní knize chybí zápis ${missing === 1 ? '1 hodiny' : `${missing} hodin`}, takže nejde ověřit, že žák chyběl. Přesto schválit?`,
    icon: 'pi pi-exclamation-triangle',
    acceptLabel: 'Schválit',
    rejectLabel: 'Zrušit',
    rejectButtonProps: { severity: 'secondary', outlined: true },
    accept: approve,
  });
}

/** "1. M chyběl(a)". */
export function lessonText(lesson: PupilLesson): string {
  return `${lesson.periodNumber}. ${lesson.subject} ${STATE_LABELS[lesson.state]}`;
}

/** A pupil's lessons of an omluvenka's day as the třídní kniha has them —
 * what the teacher approves the day on. */
@Component({
  selector: 'app-day-lessons',
  templateUrl: './day-lessons.html',
})
export class DayLessons {
  readonly lessons = input.required<PupilLesson[]>();

  protected readonly text = lessonText;
}
