import { Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  ClassBookApiService,
  ClassBookLesson,
  LessonAttendanceStatus,
  LessonMarkRequest,
  LessonPupil,
  LessonPupils,
  PhotosApiService,
} from '@soovioo/shared';
import { Avatar } from '../avatar/avatar';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';

export const TOPIC_MAX_LENGTH = 500;

/** A pupil's state in the dialog; `Present` is no mark. */
export type PupilState = LessonAttendanceStatus | 'Present';

export interface PupilMark {
  state: PupilState;
  minutes: number | null;
}

/** `label` in the list, `button` one by one, `title` in full. */
export const STATES: {
  value: PupilState;
  label: string;
  button: string;
  title: string;
  severity: 'primary' | 'danger' | 'secondary';
}[] = [
  {
    value: 'Present',
    label: 'Zde',
    button: 'Přítomen',
    title: 'Přítomen',
    severity: 'primary',
  },
  {
    value: 'Absent',
    label: 'Chybí',
    button: 'Nepřítomen',
    title: 'Nepřítomen',
    severity: 'danger',
  },
  {
    value: 'Late',
    label: 'Pozdě',
    button: 'Pozdní příchod',
    title: 'Pozdní příchod',
    severity: 'secondary',
  },
  {
    value: 'LeftEarly',
    label: 'Odchod',
    button: 'Odchod z hodiny',
    title: 'Odchod během hodiny',
    severity: 'secondary',
  },
  {
    value: 'Away',
    label: 'Mimo',
    button: 'Mimo třídu',
    title: 'Mimo třídu ze školních důvodů (soutěž, akce)',
    severity: 'secondary',
  },
];

export const hasMinutes = (state: PupilState) =>
  state === 'Late' || state === 'LeftEarly';

/** As saved; before the attendance is taken, a likely away pupil absent
 * and everyone else there. */
export function initialMarks(lesson: LessonPupils): Record<number, PupilMark> {
  return Object.fromEntries(
    lesson.pupils.map((p) => [
      p.studentId,
      {
        state:
          p.status ??
          (!lesson.attendanceTaken && p.suggestion ? 'Absent' : 'Present'),
        minutes: p.minutes,
      },
    ]),
  );
}

/** The pupils not there the whole lesson. */
export function markRequests(
  marks: Record<number, PupilMark>,
): LessonMarkRequest[] {
  return Object.entries(marks)
    .filter(([, m]) => m.state !== 'Present')
    .map(([id, m]) => ({
      studentId: Number(id),
      status: m.state as LessonAttendanceStatus,
      minutes: hasMinutes(m.state) ? m.minutes : null,
    }));
}

/** Zápis hodiny: its topic and who was there — the class's (or group's)
 * pupils all there unless marked, or one by one as the teacher calls the
 * names. Finishing the last one saves. */
@Component({
  selector: 'app-lesson-dialog',
  imports: [
    Avatar,
    Button,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    InputNumber,
    Loading,
    SelectButton,
    Textarea,
  ],
  templateUrl: './lesson-dialog.html',
})
export class LessonDialog {
  private readonly api = inject(ClassBookApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  /** The lesson as saved. */
  readonly saved = output<ClassBookLesson>();

  protected readonly topicMaxLength = TOPIC_MAX_LENGTH;
  protected readonly states = STATES;
  protected readonly hasMinutes = hasMinutes;
  protected readonly modeOptions = [
    { label: 'Seznam', value: 'list' },
    { label: 'Po jednom', value: 'one' },
  ];

  protected readonly lesson = signal<ClassBookLesson | null>(null);
  protected readonly pupils = signal<LessonPupils | null>(null);
  protected readonly marks = signal<Record<number, PupilMark>>({});
  protected readonly mode = signal<'list' | 'one'>('list');
  /** The pupil "po jednom" is at. */
  protected readonly index = signal(0);
  protected readonly saving = signal(false);
  protected topic = '';

  protected readonly current = computed(
    () => this.pupils()?.pupils[this.index()] ?? null,
  );
  protected readonly absentCount = computed(
    () =>
      Object.values(this.marks()).filter((m) => m.state === 'Absent').length,
  );

  open(lesson: ClassBookLesson): void {
    this.topic = lesson.topic ?? '';
    this.pupils.set(null);
    this.marks.set({});
    this.mode.set('list');
    this.index.set(0);
    this.lesson.set(lesson);
    this.api.getPupils(lesson).subscribe({
      next: (pupils) => {
        this.marks.set(initialMarks(pupils));
        this.pupils.set(pupils);
      },
      error: (err) => this.notify.actionFailed('getPupils failed', err),
    });
  }

  protected close(): void {
    this.lesson.set(null);
  }

  protected heading(lesson: ClassBookLesson): string {
    return `${lesson.periodNumber}. hodina · ${lesson.subjectName} · ${lesson.who}`;
  }

  protected name(pupil: LessonPupil): string {
    return `${pupil.lastName} ${pupil.firstName}`;
  }

  protected photoUrl(pupil: LessonPupil): string | null {
    return pupil.photo
      ? this.photosApi.studentPhotoUrl(pupil.studentId, pupil.photo)
      : null;
  }

  protected mark(pupil: LessonPupil): PupilMark {
    return this.marks()[pupil.studentId] ?? { state: 'Present', minutes: null };
  }

  protected setState(pupil: LessonPupil, state: PupilState | null): void {
    if (state) {
      this.update(pupil, { state, minutes: this.mark(pupil).minutes });
    }
  }

  protected setMinutes(pupil: LessonPupil, minutes: number | null): void {
    this.update(pupil, { ...this.mark(pupil), minutes });
  }

  /** "Po jednom": the state, and on to the next pupil — unless it needs
   * the minutes first. */
  protected choose(pupil: LessonPupil, state: PupilState): void {
    this.setState(pupil, state);
    if (!hasMinutes(state)) {
      this.next();
    }
  }

  protected next(): void {
    const count = this.pupils()?.pupils.length ?? 0;
    if (this.index() + 1 < count) {
      this.index.update((i) => i + 1);
    } else {
      this.save();
    }
  }

  protected back(): void {
    this.index.update((i) => Math.max(0, i - 1));
  }

  protected setMode(mode: 'list' | 'one' | null): void {
    if (mode) {
      this.mode.set(mode);
      this.index.set(0);
    }
  }

  protected stateTitle(state: PupilState): string {
    return STATES.find((s) => s.value === state)?.title ?? '';
  }

  /** Saves the topic and the attendance; `remove` takes back a record
   * without attendance. */
  protected save(remove = false): void {
    const lesson = this.lesson();
    if (!lesson) {
      return;
    }
    const pupils = this.pupils();
    this.saving.set(true);
    this.api
      .saveLesson({
        date: lesson.date,
        periodNumber: lesson.periodNumber,
        schoolClassId: lesson.schoolClassId,
        studentGroupId: lesson.studentGroupId,
        topic: remove ? '' : this.topic.trim(),
        attendance:
          remove || !pupils || pupils.pupils.length === 0
            ? null
            : markRequests(this.marks()),
      })
      .subscribe({
        next: (saved) => {
          this.saving.set(false);
          this.lesson.set(null);
          this.saved.emit(saved);
        },
        error: (err) => {
          this.saving.set(false);
          this.notify.actionFailed('saveLesson failed', err);
        },
      });
  }

  private update(pupil: LessonPupil, mark: PupilMark): void {
    this.marks.update((marks) => ({ ...marks, [pupil.studentId]: mark }));
  }
}
