import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  ClassBookApiService,
  ClassBookClass,
  ClassBookDay,
  AbsenceStatus,
  ClassBookLesson,
  LessonAttendanceStatus,
  LessonMark,
  formatDate,
} from '@soovioo/shared';
import { map } from 'rxjs';
import { toDateOnly } from '../departures/retention-labels';
import { Loading } from '../loading/loading';
import { DESKTOP_QUERY, mediaQuery } from '../media-query';
import { NotifyService } from '../notify.service';
import { AttendanceBar, barLabel } from './attendance-bar';
import { LessonDialog } from './lesson-dialog';

const WEEKDAYS = ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'];

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

/** Monday of the date's week. */
export function mondayOf(date: Date): Date {
  const monday = new Date(date);
  monday.setDate(monday.getDate() - ((monday.getDay() + 6) % 7));
  return monday;
}

/** The week by period: the periods any of its days has, each with the
 * days' lessons then (a class may have several, one per group). */
export function weekRows(
  days: ClassBookDay[],
): { period: number; cells: ClassBookLesson[][] }[] {
  const periods = [
    ...new Set(days.flatMap((d) => d.lessons.map((l) => l.periodNumber))),
  ].sort((a, b) => a - b);
  return periods.map((period) => ({
    period,
    cells: days.map((d) => d.lessons.filter((l) => l.periodNumber === period)),
  }));
}

type View = 'detail' | 'compact';
type Range = 'day' | 'week';

const VIEW_KEY = 'soovioo.classBook.view';
const RANGE_KEY = 'soovioo.classBook.range';

function stored<T extends string>(key: string, values: T[]): T | null {
  try {
    const value = localStorage.getItem(key) as T | null;
    return value && values.includes(value) ? value : null;
  } catch {
    return null;
  }
}

function store(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // Not remembered, then.
  }
}

/** "8:00–8:45". */
export function lessonTime(lesson: ClassBookLesson): string {
  const time = (t: string) => t.slice(0, 5).replace(/^0/, '');
  return lesson.startTime && lesson.endTime
    ? `${time(lesson.startTime)}–${time(lesson.endTime)}`
    : '';
}

const MARK_LABELS: Record<LessonAttendanceStatus, string> = {
  Absent: 'Chybí',
  Late: 'Pozdě',
  LeftEarly: 'Odchod',
  Away: 'Mimo třídu',
};

const EXCUSE_TAGS: Partial<
  Record<
    AbsenceStatus,
    { label: string; severity: 'warn' | 'success' | 'danger' }
  >
> = {
  PendingExcuse: { label: 'omluvenka čeká', severity: 'warn' },
  Excused: { label: 'omluveno', severity: 'success' },
  Rejected: { label: 'omluvenka zamítnuta', severity: 'danger' },
};

/** The marks by state: absent, late, leaving, away. */
export function attendanceGroups(
  lesson: ClassBookLesson,
): { label: string; marks: LessonMark[] }[] {
  return (Object.keys(MARK_LABELS) as LessonAttendanceStatus[])
    .map((status) => ({
      label: MARK_LABELS[status],
      marks: lesson.marks.filter((m) => m.status === status),
    }))
    .filter((group) => group.marks.length > 0);
}

/** Třídní kniha: a class's lessons of a day — or the teacher's own — each
 * with its topic and who was there, written by whoever teaches it (the třídní and the
 * management any time). `?day=yyyy-MM-dd&class=<id>` opens that day and
 * class. Compact (a phone's default): a row a lesson with its attendance as
 * a bar, a day or a week. */
@Component({
  selector: 'app-class-book',
  imports: [
    AttendanceBar,
    Button,
    DatePicker,
    FormsModule,
    LessonDialog,
    Loading,
    Message,
    NgTemplateOutlet,
    Select,
    SelectButton,
    Tag,
  ],
  templateUrl: './class-book.html',
  styleUrl: './class-book.scss',
})
export class ClassBook {
  private readonly api = inject(ClassBookApiService);
  private readonly notify = inject(NotifyService);
  private readonly auth = inject(AuthApiService);
  private readonly dialog = viewChild.required(LessonDialog);

  protected readonly date = signal(new Date());
  /** Null = the caller's own lessons. */
  protected readonly classId = signal<number | null>(null);
  protected readonly classes = signal<ClassBookClass[]>([]);
  /** The day, or the week's days. */
  protected readonly days = signal<ClassBookDay[] | null>(null);

  protected readonly view = signal<View>(
    stored<View>(VIEW_KEY, ['detail', 'compact']) ??
      (mediaQuery(DESKTOP_QUERY)() ? 'detail' : 'compact'),
  );
  protected readonly range = signal<Range>(
    stored<Range>(RANGE_KEY, ['day', 'week']) ?? 'day',
  );
  protected readonly week = computed(
    () => this.view() === 'compact' && this.range() === 'week',
  );
  /** Days side by side; narrower, one under another. */
  protected readonly gridFits = mediaQuery('(min-width: 360px)');
  protected readonly grid = computed(() => weekRows(this.days() ?? []));
  /** The compact row showing its topic and attendance. */
  private readonly expanded = signal<string | null>(null);

  protected readonly viewOptions = [
    { label: 'Podrobně', value: 'detail' },
    { label: 'Kompaktně', value: 'compact' },
  ];
  protected readonly rangeOptions = [
    { label: 'Den', value: 'day' },
    { label: 'Týden', value: 'week' },
  ];

  protected readonly classOptions = computed(() => [
    { id: null, name: 'Moje hodiny' },
    ...this.classes(),
  ]);

  protected readonly dateLabel = computed(() => {
    const d = this.date();
    if (this.week()) {
      const monday = mondayOf(d);
      const friday = new Date(monday);
      friday.setDate(monday.getDate() + 4);
      return `${formatDate(toDateOnly(monday), 'date')} – ${formatDate(toDateOnly(friday), 'date')}`;
    }
    return `${WEEKDAYS[d.getDay()]} ${formatDate(toDateOnly(d), 'date')}`;
  });

  protected readonly noLessons = computed(() =>
    this.classId() === null
      ? 'Tento den neučíte žádnou hodinu.'
      : 'Tento den nejsou žádné hodiny.',
  );

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
    if (this.week()) {
      const date = new Date(this.date());
      date.setDate(date.getDate() + 7 * direction);
      this.goTo(date);
    } else {
      this.goTo(nextSchoolDay(this.date(), direction));
    }
  }

  protected setView(view: View): void {
    store(VIEW_KEY, view);
    this.view.set(view);
    this.load();
  }

  protected setRange(range: Range): void {
    store(RANGE_KEY, range);
    this.range.set(range);
    this.load();
  }

  protected lessonKey(lesson: ClassBookLesson): string {
    return [
      lesson.date,
      lesson.periodNumber,
      lesson.schoolClassId,
      lesson.studentGroupId,
      lesson.gone,
    ].join('|');
  }

  protected isExpanded(lesson: ClassBookLesson): boolean {
    return this.expanded() === this.lessonKey(lesson);
  }

  /** Writing it for whoever may, else its topic and attendance. */
  protected tap(lesson: ClassBookLesson): void {
    if (lesson.canWrite) {
      this.dialog().open(lesson);
    } else {
      const key = this.lessonKey(lesson);
      this.expanded.update((open) => (open === key ? null : key));
    }
  }

  /** As a tap on its day's row, the day shown when there's nothing to
   * write. */
  protected openFromWeek(lesson: ClassBookLesson): void {
    if (lesson.canWrite) {
      this.dialog().open(lesson);
      return;
    }
    this.range.set('day');
    this.expanded.set(this.lessonKey(lesson));
    this.goTo(new Date(`${lesson.date}T00:00:00`), false);
  }

  /** Its subject and, where it isn't plain, whose lesson. */
  protected showWho(lesson: ClassBookLesson): boolean {
    return this.classId() === null || lesson.studentGroupId !== null;
  }

  protected abbreviation(lesson: ClassBookLesson): string {
    return lesson.subjectAbbreviation || lesson.subjectName;
  }

  protected rowLabel(lesson: ClassBookLesson): string {
    const action = lesson.canWrite
      ? this.written(lesson)
        ? 'Upravit '
        : 'Zapsat '
      : '';
    const state = lesson.gone ? lesson.label : barLabel(lesson);
    return `${action}${this.heading(lesson)}, ${state}`;
  }

  /** "po 16. 11.". */
  protected dayLabel(day: ClassBookDay): string {
    return `${this.weekday(day)} ${this.shortDate(day)}`;
  }

  protected weekday(day: ClassBookDay): string {
    return WEEKDAYS[new Date(`${day.date}T00:00:00`).getDay()];
  }

  /** "16. 11.". */
  protected shortDate(day: ClassBookDay): string {
    const date = new Date(`${day.date}T00:00:00`);
    return `${date.getDate()}. ${date.getMonth() + 1}.`;
  }

  protected goTo(date: Date | null, collapse = true): void {
    if (date) {
      if (collapse) {
        this.expanded.set(null);
      }
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

  protected groups(lesson: ClassBookLesson) {
    return attendanceGroups(lesson);
  }

  protected markName(mark: LessonMark): string {
    return mark.minutes
      ? `${mark.studentName} (${mark.minutes} min)`
      : mark.studentName;
  }

  protected excuseTag(mark: LessonMark) {
    return mark.excuseStatus ? EXCUSE_TAGS[mark.excuseStatus] : undefined;
  }

  protected written(lesson: ClassBookLesson): boolean {
    return !!lesson.topic || lesson.attendanceTaken;
  }

  /** The lesson as just saved. */
  protected replace(saved: ClassBookLesson): void {
    const same = (l: ClassBookLesson) =>
      l.periodNumber === saved.periodNumber &&
      l.schoolClassId === saved.schoolClassId &&
      l.studentGroupId === saved.studentGroupId &&
      l.date === saved.date;
    this.days.update(
      (days) =>
        days?.map((day) => ({
          ...day,
          lessons: day.lessons.map((l) => (same(l) && !l.gone ? saved : l)),
        })) ?? null,
    );
  }

  private load(): void {
    this.days.set(null);
    const date = toDateOnly(this.date());
    const classId = this.classId();
    const week = this.week();
    const days = week
      ? this.api.getWeek(date, classId)
      : this.api.getDay(date, classId).pipe(map((day) => [day]));
    days.subscribe({
      next: (days) => {
        if (
          date === toDateOnly(this.date()) &&
          classId === this.classId() &&
          week === this.week()
        ) {
          this.days.set(days);
        }
      },
      error: (err) =>
        this.notify.actionFailed(
          week ? 'getWeek failed' : 'getDay failed',
          err,
        ),
    });
  }
}
