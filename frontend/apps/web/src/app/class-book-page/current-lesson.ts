import { Component, inject, signal } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import {
  AuthApiService,
  ClassBookApiService,
  ClassBookLesson,
} from '@soovioo/shared';
import { lessonTime } from './class-book-page';
import { LessonDialog } from './lesson-dialog';

/** Whether the lesson is on at `now` (its times "08:00:00"). */
export function isOn(lesson: ClassBookLesson, now = new Date()): boolean {
  const time = `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`;
  return (
    !!lesson.startTime &&
    !!lesson.endTime &&
    lesson.startTime.slice(0, 5) <= time &&
    time < lesson.endTime.slice(0, 5)
  );
}

/** Domů: the teacher's lesson going on now, else their next one today,
 * to write its attendance and topic. Nothing when none is left. */
@Component({
  selector: 'app-current-lesson',
  imports: [Button, LessonDialog],
  templateUrl: './current-lesson.html',
})
export class CurrentLesson {
  private readonly api = inject(ClassBookApiService);

  protected readonly lesson = signal<ClassBookLesson | null>(null);

  constructor() {
    if (inject(AuthApiService).can('ClassBookWrite')) {
      this.api.getNow().subscribe({
        next: (lesson) => this.lesson.set(lesson),
        // Only a shortcut: the třídní kniha is a click away.
        error: () => this.lesson.set(null),
      });
    }
  }

  protected title(lesson: ClassBookLesson): string {
    return isOn(lesson) ? 'Právě učíte' : 'Další hodina';
  }

  protected time(lesson: ClassBookLesson): string {
    return lessonTime(lesson);
  }

  protected written(lesson: ClassBookLesson): boolean {
    return !!lesson.topic || lesson.attendanceTaken;
  }
}
