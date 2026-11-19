import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  ClassBookApiService,
  ClassBookClass,
  ClassBookDay,
  ClassBookLesson,
  formatDate,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { toDateOnly } from '../departures/retention-labels';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';

const WEEKDAYS = ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'];

export const TOPIC_MAX_LENGTH = 500;

/** The school day `step` days of school away (weekends skipped). */
export function nextSchoolDay(date: Date, step: number): Date {
  const d = new Date(date);
  do {
    d.setDate(d.getDate() + step);
  } while (d.getDay() === 0 || d.getDay() === 6);
  return d;
}

/** `yyyy-MM-dd`, else today — at the weekend the Friday before. */
export function classBookDay(value: string | null, now = new Date()): Date {
  const day = value ? new Date(`${value}T00:00:00`) : null;
  if (day && !isNaN(day.getTime())) {
    return day;
  }
  const today = new Date(now);
  today.setHours(0, 0, 0, 0);
  return today.getDay() === 0 || today.getDay() === 6
    ? nextSchoolDay(today, -1)
    : today;
}

/** "8:00–8:45". */
export function lessonTime(lesson: ClassBookLesson): string {
  const time = (t: string) => t.slice(0, 5).replace(/^0/, '');
  return lesson.startTime && lesson.endTime
    ? `${time(lesson.startTime)}–${time(lesson.endTime)}`
    : '';
}

/** Třídní kniha: a class's lessons of a day — or the teacher's own — each
 * with its topic, written by whoever teaches it (the třídní and the
 * management any time). `?day=yyyy-MM-dd&class=<id>` opens that day and
 * class. */
@Component({
  selector: 'app-class-book-page',
  imports: [
    AppIcon,
    Button,
    DatePicker,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Loading,
    Message,
    Select,
    Tag,
    Textarea,
  ],
  templateUrl: './class-book-page.html',
})
export class ClassBookPage {
  private readonly api = inject(ClassBookApiService);
  private readonly notify = inject(NotifyService);
  private readonly auth = inject(AuthApiService);

  protected readonly topicMaxLength = TOPIC_MAX_LENGTH;

  protected readonly date = signal(new Date());
  /** Null = the caller's own lessons. */
  protected readonly classId = signal<number | null>(null);
  protected readonly classes = signal<ClassBookClass[]>([]);
  protected readonly day = signal<ClassBookDay | null>(null);

  protected readonly classOptions = computed(() => [
    { id: null, name: 'Moje hodiny' },
    ...this.classes(),
  ]);

  protected readonly dateLabel = computed(() => {
    const d = this.date();
    return `${WEEKDAYS[d.getDay()]} ${formatDate(toDateOnly(d), 'date')}`;
  });

  protected readonly editing = signal<ClassBookLesson | null>(null);
  protected topic = '';
  protected readonly saving = signal(false);

  constructor() {
    const query = inject(ActivatedRoute).snapshot.queryParamMap;
    this.date.set(classBookDay(query.get('day')));
    const classParam = Number(query.get('class'));
    // The management teaches little or nothing: they start at a class.
    const startsAtClass =
      classParam > 0 || this.auth.scope('ClassBookRead') === 'School';
    this.api.getClasses().subscribe({
      next: (classes) => {
        this.classes.set(classes);
        if (startsAtClass) {
          this.classId.set(
            classes.find((c) => c.id === classParam)?.id ??
              classes[0]?.id ??
              null,
          );
        }
        this.load();
      },
      error: (err) => this.notify.actionFailed('getClasses failed', err),
    });
  }

  protected step(direction: number): void {
    this.goTo(nextSchoolDay(this.date(), direction));
  }

  protected goTo(date: Date | null): void {
    if (date) {
      this.date.set(date);
      this.load();
    }
  }

  protected selectClass(classId: number | null): void {
    this.classId.set(classId);
    this.load();
  }

  protected time(lesson: ClassBookLesson): string {
    return lessonTime(lesson);
  }

  /** "Zapsal(a) Jana Malá 2. 11. 9:12". */
  protected recorded(lesson: ClassBookLesson): string {
    if (!lesson.recordedAt) {
      return '';
    }
    const at = formatDate(lesson.recordedAt, 'dayMonthTime');
    return lesson.recordedByName
      ? `Zapsal(a) ${lesson.recordedByName}, ${at}`
      : `Zapsáno ${at}`;
  }

  protected heading(lesson: ClassBookLesson): string {
    return `${lesson.periodNumber}. hodina · ${lesson.subjectName} · ${lesson.who}`;
  }

  protected edit(lesson: ClassBookLesson): void {
    this.topic = lesson.topic ?? '';
    this.editing.set(lesson);
  }

  protected close(): void {
    this.editing.set(null);
  }

  /** Saves the topic; `remove` takes the record back. */
  protected save(remove = false): void {
    const lesson = this.editing();
    if (!lesson) {
      return;
    }
    this.saving.set(true);
    this.api
      .saveLesson({
        date: lesson.date,
        periodNumber: lesson.periodNumber,
        schoolClassId: lesson.schoolClassId,
        studentGroupId: lesson.studentGroupId,
        topic: remove ? '' : this.topic.trim(),
      })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.editing.set(null);
          this.day.update(
            (day) =>
              day && {
                ...day,
                lessons: day.lessons.map((l) => (l === lesson ? saved : l)),
              },
          );
        },
        error: (err) => {
          this.saving.set(false);
          this.notify.actionFailed('saveLesson failed', err);
        },
      });
  }

  private load(): void {
    this.day.set(null);
    const date = toDateOnly(this.date());
    const classId = this.classId();
    this.api.getDay(date, classId).subscribe({
      next: (day) => {
        if (date === toDateOnly(this.date()) && classId === this.classId()) {
          this.day.set(day);
        }
      },
      error: (err) => this.notify.actionFailed('getDay failed', err),
    });
  }
}
