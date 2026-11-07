import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { Tag } from '@openng/optimus-ui/tag';
import {
  SchoolClass,
  SchoolClassesApiService,
  SchoolPeriod,
  SchoolPeriodsApiService,
  Subject,
  SubjectsApiService,
  SubstitutionCandidates,
  SubstitutionDay,
  SubstitutionLesson,
  SubstitutionsApiService,
  Teacher,
  TeachersApiService,
  formatDate,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { toDateOnly } from '../departures/retention-labels';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';

type ChangeKind = 'cover' | 'cancel' | 'move' | 'merge';

type ViewBy = 'classes' | 'teachers';

/** A row of the day's grid: a class or a teacher, its lessons by period. */
interface GridRow {
  id: number;
  name: string;
  absent: boolean;
  cells: Map<number, SubstitutionLesson[]>;
}

interface LessonForm {
  lesson: SubstitutionLesson | null;
  /** Which lesson of the timetable; for an extra one its class. */
  date: string;
  periodNumber: number | null;
  schoolClassId: number | null;
  studentGroupId: number | null;
  kind: ChangeKind;
  teacherId: number | null;
  subjectId: number | null;
  classroomId: number | null;
  moveDate: Date | null;
  movePeriod: number | null;
  /** "c:5" a class, "g:7" a group. */
  mergeWith: string | null;
}

interface AbsenceForm {
  teacherId: number | null;
  dateFrom: Date;
  dateTo: Date;
  periodFrom: number | null;
  periodTo: number | null;
}

const WEEKDAYS = ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'];

function nextSchoolDay(date: Date, step: number): Date {
  const d = new Date(date);
  do {
    d.setDate(d.getDate() + step);
  } while (d.getDay() === 0 || d.getDay() === 6);
  return d;
}

function dayOf(value: string | null): Date {
  const day = value ? new Date(`${value}T00:00:00`) : null;
  return day && !isNaN(day.getTime()) ? day : today();
}

function today(): Date {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d.getDay() === 0 || d.getDay() === 6 ? nextSchoolDay(d, 1) : d;
}

/** Administrace → Výuka → Suplování: a day's absent teachers and its
 * lessons in a grid of classes (or teachers) by periods, with substitutes
 * offered, published when ready. */
@Component({
  selector: 'app-substitutions-panel',
  imports: [
    Button,
    Checkbox,
    DatePicker,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Loading,
    Message,
    Select,
    SelectButton,
    Tag,
  ],
  templateUrl: './substitutions-panel.html',
})
export class SubstitutionsPanel {
  private readonly api = inject(SubstitutionsApiService);
  private readonly notify = inject(NotifyService);
  private readonly confirmation = inject(ConfirmationService);

  /** `?day=yyyy-MM-dd` opens that day. */
  protected readonly date = signal(
    dayOf(inject(ActivatedRoute).snapshot.queryParamMap.get('day')),
  );
  protected readonly day = signal<SubstitutionDay | null>(null);
  protected readonly busy = signal(false);
  protected readonly viewBy = signal<ViewBy>('classes');
  /** Otherwise only the lessons without a substitute. */
  protected readonly showAll = signal(false);
  protected readonly viewByOptions = [
    { label: 'Třídy', value: 'classes' },
    { label: 'Učitelé', value: 'teachers' },
  ];

  protected readonly teachers = signal<Teacher[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly periods = signal<SchoolPeriod[]>([]);

  protected readonly dateLabel = computed(() => {
    const d = this.date();
    return `${WEEKDAYS[d.getDay()]} ${formatDate(toDateOnly(d), 'date')}`;
  });
  protected readonly status = computed(() => {
    const day = this.day();
    if (!day) {
      return null;
    }
    if (!day.publishedAt) {
      return { label: 'Nezveřejněno', severity: 'secondary' as const };
    }
    return day.unpublished
      ? { label: 'Úpravy nejsou zveřejněné', severity: 'warn' as const }
      : {
          label: `Zveřejněno ${formatDate(day.publishedAt, 'dayMonthTime')}`,
          severity: 'success' as const,
        };
  });
  /** Classes, or the teachers teaching that day or away, with their lessons. */
  protected readonly rows = computed<GridRow[]>(() => {
    const day = this.day();
    if (!day) {
      return [];
    }
    const absent = new Set(day.absences.map((a) => a.teacherId));
    const row = (
      id: number,
      name: string,
      of: (l: SubstitutionLesson) => boolean,
    ): GridRow => {
      const cells = new Map<number, SubstitutionLesson[]>();
      for (const lesson of day.lessons.filter(
        (l) => of(l) && (this.showAll() || l.needsCover),
      )) {
        cells.set(lesson.periodNumber, [
          ...(cells.get(lesson.periodNumber) ?? []),
          lesson,
        ]);
      }
      return {
        id,
        name,
        absent: this.viewBy() === 'teachers' && absent.has(id),
        cells,
      };
    };
    const shown = (rows: GridRow[]) =>
      this.showAll() ? rows : rows.filter((r) => r.cells.size > 0);
    if (this.viewBy() === 'classes') {
      return shown(
        day.classes.map((c) =>
          row(c.id, c.name, (l) => l.classIds.includes(c.id)),
        ),
      );
    }
    const ids = new Set<number>(absent);
    for (const l of day.lessons) {
      for (const id of [l.teacherId, l.originalTeacherId]) {
        if (id !== null) {
          ids.add(id);
        }
      }
    }
    return shown(
      [...ids].map((id) =>
        row(
          id,
          this.teacherName(id),
          (l) =>
            (l.teacherId === id && !l.gone) ||
            (l.originalTeacherId === id && (l.teacherId !== id || l.gone)),
        ),
      ),
    ).sort((a, b) => a.name.localeCompare(b.name, 'cs'));
  });
  protected readonly freeByPeriod = computed(
    () => new Map((this.day()?.free ?? []).map((f) => [f.period, f])),
  );
  protected readonly needCover = computed(
    () => this.day()?.lessons.filter((l) => l.needsCover).length ?? 0,
  );

  protected readonly periodOptions = computed(() =>
    this.periods().map((p) => ({
      value: p.periodNumber,
      label: `${p.periodNumber}. hodina`,
    })),
  );
  protected readonly teacherOptions = computed(() =>
    this.teachers().map((t) => ({ value: t.id, label: t.name })),
  );
  protected readonly classOptions = computed(() =>
    this.classes().map((c) => ({ value: c.id, label: c.name })),
  );
  protected readonly kindOptions = [
    { value: 'cover', label: 'Supl, jiný předmět nebo učebna' },
    { value: 'cancel', label: 'Odpadá' },
    { value: 'move', label: 'Přesun na jinou hodinu' },
    { value: 'merge', label: 'Spojení s jinou třídou nebo skupinou' },
  ];

  protected readonly absenceForm = signal<AbsenceForm | null>(null);
  protected readonly lessonForm = signal<LessonForm | null>(null);
  protected readonly candidates = signal<SubstitutionCandidates | null>(null);

  protected readonly substituteOptions = computed(() => {
    const form = this.lessonForm();
    const lesson = form?.lesson;
    const offered = this.candidates()?.teachers ?? [];
    const timetable =
      lesson && !lesson.change?.extra
        ? [
            {
              value: null as number | null,
              label: `Podle rozvrhu (${this.teacherName(lesson.originalTeacherId)})`,
            },
          ]
        : [];
    return [
      ...timetable,
      ...offered.map((t) => ({
        value: t.teacherId as number | null,
        label: `${t.name} (${[
          t.qualified ? 'aprobace' : '',
          t.teachesThatDay ? 'ten den učí' : 'ten den neučí',
          `suplování v měsíci: ${t.substitutionsThisMonth}`,
        ]
          .filter((x) => x)
          .join(', ')})`,
      })),
    ];
  });
  protected readonly subjectOptions = computed(() => [
    ...(this.lessonForm()?.lesson && !this.lessonForm()?.lesson?.change?.extra
      ? [{ value: null as number | null, label: 'Podle rozvrhu' }]
      : []),
    ...this.subjects().map((s) => ({
      value: s.id as number | null,
      label: s.name,
    })),
  ]);
  protected readonly roomOptions = computed(() => [
    { value: null as number | null, label: 'Podle rozvrhu' },
    ...(this.candidates()?.classrooms ?? []).map((r) => ({
      value: r.id as number | null,
      label: r.suits ? `${r.name} (pro předmět)` : r.name,
    })),
  ]);
  protected readonly mergeOptions = computed(() => {
    const form = this.lessonForm();
    if (!form) {
      return [];
    }
    return (this.day()?.lessons ?? [])
      .filter(
        (l) =>
          l.periodNumber === form.periodNumber &&
          !l.gone &&
          !l.change?.mergedWithClassId &&
          !l.change?.mergedWithGroupId &&
          !(
            l.schoolClassId === form.schoolClassId &&
            l.studentGroupId === form.studentGroupId
          ),
      )
      .map((l) => ({
        value:
          l.studentGroupId !== null
            ? `g:${l.studentGroupId}`
            : `c:${l.schoolClassId}`,
        label: `${l.who} — ${l.subject}${l.teacher ? `, ${l.teacher}` : ''}`,
      }));
  });

  constructor() {
    inject(TeachersApiService)
      .getTeachers()
      .subscribe({
        next: (t) => this.teachers.set(t),
        error: (err) => console.error('getTeachers failed', err),
      });
    inject(SubjectsApiService)
      .getSubjects()
      .subscribe({
        next: (s) => this.subjects.set(s),
        error: (err) => console.error('getSubjects failed', err),
      });
    inject(SchoolClassesApiService)
      .getSchoolClasses()
      .subscribe({
        next: (c) => this.classes.set(c),
        error: (err) => console.error('getSchoolClasses failed', err),
      });
    inject(SchoolPeriodsApiService)
      .getPeriods()
      .subscribe({
        next: (p) => this.periods.set(p),
        error: (err) => console.error('getPeriods failed', err),
      });
    this.load();
  }

  private dateOnly(): string {
    return toDateOnly(this.date());
  }

  private load(): void {
    const date = this.dateOnly();
    this.api.getDay(date).subscribe({
      next: (day) => {
        if (day.date === this.dateOnly()) {
          this.day.set(day);
        }
      },
      error: (err) => this.notify.actionFailed('getDay failed', err),
    });
  }

  protected step(direction: number): void {
    this.goTo(nextSchoolDay(this.date(), direction));
  }

  protected goTo(date: Date | null): void {
    if (!date) {
      return;
    }
    this.date.set(date);
    this.day.set(null);
    this.load();
  }

  private run(call: Observable<SubstitutionDay>, after?: () => void): void {
    this.busy.set(true);
    call.subscribe({
      next: (day) => {
        this.busy.set(false);
        this.day.set(day);
        after?.();
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('substitutions failed', err);
      },
    });
  }

  protected teacherName(id: number | null): string {
    return this.teachers().find((t) => t.id === id)?.name ?? '—';
  }

  protected absenceTime(a: {
    dateFrom: string;
    dateTo: string;
    periodFrom: number | null;
    periodTo: number | null;
  }): string {
    const days =
      a.dateFrom === a.dateTo
        ? formatDate(a.dateFrom, 'dayMonth')
        : `${formatDate(a.dateFrom, 'dayMonth')}–${formatDate(a.dateTo, 'dayMonth')}`;
    if (a.periodFrom === null && a.periodTo === null) {
      return days;
    }
    return `${days}, ${a.periodFrom ?? 'od začátku'}.–${a.periodTo ?? 'do konce'}. hodina`;
  }

  protected newAbsence(): void {
    this.absenceForm.set({
      teacherId: null,
      dateFrom: this.date(),
      dateTo: this.date(),
      periodFrom: null,
      periodTo: null,
    });
  }

  protected saveAbsence(form: AbsenceForm): void {
    if (form.teacherId === null) {
      return;
    }
    this.run(
      this.api.addAbsence({
        teacherId: form.teacherId,
        dateFrom: toDateOnly(form.dateFrom),
        dateTo: toDateOnly(form.dateTo),
        periodFrom: form.periodFrom,
        periodTo: form.periodTo,
        day: this.dateOnly(),
      }),
      () => this.absenceForm.set(null),
    );
  }

  protected deleteAbsence(id: number): void {
    this.busy.set(true);
    this.api.deleteAbsence(id).subscribe({
      next: () => {
        this.busy.set(false);
        this.load();
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('deleteAbsence failed', err);
      },
    });
  }

  protected edit(lesson: SubstitutionLesson): void {
    const change = lesson.change;
    const kind: ChangeKind = change?.cancelled
      ? 'cancel'
      : change?.movedToDate
        ? 'move'
        : change?.mergedWithClassId || change?.mergedWithGroupId
          ? 'merge'
          : 'cover';
    this.lessonForm.set({
      lesson,
      date: change?.date ?? this.dateOnly(),
      periodNumber: change?.period ?? lesson.periodNumber,
      schoolClassId: change ? change.classId : lesson.schoolClassId,
      studentGroupId: change ? change.groupId : lesson.studentGroupId,
      kind,
      teacherId: change?.teacherId ?? null,
      subjectId: change?.subjectId ?? null,
      classroomId: change?.classroomId ?? null,
      moveDate: change?.movedToDate
        ? new Date(`${change.movedToDate}T00:00:00`)
        : this.date(),
      movePeriod: change?.movedToPeriod ?? null,
      mergeWith: change?.mergedWithGroupId
        ? `g:${change.mergedWithGroupId}`
        : change?.mergedWithClassId
          ? `c:${change.mergedWithClassId}`
          : null,
    });
    this.loadCandidates(lesson.periodNumber, lesson, null);
  }

  protected newExtra(schoolClassId: number, periodNumber: number): void {
    const form: LessonForm = {
      lesson: null,
      date: this.dateOnly(),
      periodNumber,
      schoolClassId,
      studentGroupId: null,
      kind: 'cover',
      teacherId: null,
      subjectId: null,
      classroomId: null,
      moveDate: null,
      movePeriod: null,
      mergeWith: null,
    };
    this.lessonForm.set(form);
    this.extraChanged(form);
  }

  /** An extra lesson's teachers and classrooms once it's clear when and what. */
  protected extraChanged(form: LessonForm): void {
    if (form.periodNumber !== null && form.schoolClassId !== null) {
      this.loadCandidates(
        form.periodNumber,
        { schoolClassId: form.schoolClassId, studentGroupId: null },
        form.subjectId,
      );
    }
  }

  private loadCandidates(
    period: number,
    lesson: { schoolClassId: number | null; studentGroupId: number | null },
    subjectId: number | null,
  ): void {
    this.candidates.set(null);
    this.api
      .getCandidates(this.dateOnly(), period, lesson, subjectId)
      .subscribe({
        next: (c) => this.candidates.set(c),
        error: (err) => console.error('getCandidates failed', err),
      });
  }

  protected saveLesson(form: LessonForm): void {
    if (form.periodNumber === null) {
      return;
    }
    const [mergeKind, mergeId] = (form.mergeWith ?? ':').split(':');
    const cover = form.kind === 'cover';
    this.run(
      this.api.saveChange({
        date: form.date,
        periodNumber: form.periodNumber,
        schoolClassId: form.schoolClassId,
        studentGroupId: form.studentGroupId,
        extra: form.lesson === null || !!form.lesson.change?.extra,
        cancelled: form.kind === 'cancel',
        teacherId: cover ? form.teacherId : null,
        subjectId: cover ? form.subjectId : null,
        classroomId: cover ? form.classroomId : null,
        movedToDate:
          form.kind === 'move' && form.moveDate
            ? toDateOnly(form.moveDate)
            : null,
        movedToPeriod: form.kind === 'move' ? form.movePeriod : null,
        mergedWithClassId:
          form.kind === 'merge' && mergeKind === 'c' ? Number(mergeId) : null,
        mergedWithGroupId:
          form.kind === 'merge' && mergeKind === 'g' ? Number(mergeId) : null,
      }),
      () => this.lessonForm.set(null),
    );
  }

  protected revert(changeId: number): void {
    this.run(this.api.deleteChange(changeId), () => this.lessonForm.set(null));
  }

  /** "3. hodina 5.A M, Novák" */
  protected lessonLabel(l: SubstitutionLesson): string {
    return `${l.periodNumber}. hodina ${l.who} ${l.subject}${l.teacher ? `, ${l.teacher}` : ''}`;
  }

  protected fill(): void {
    this.busy.set(true);
    this.api.fill(this.dateOnly()).subscribe({
      next: ({ day, unfilled }) => {
        this.busy.set(false);
        this.day.set(day);
        if (unfilled > 0) {
          this.notify.warn(
            `Pro ${unfilled} ${unfilled === 1 ? 'hodinu' : unfilled < 5 ? 'hodiny' : 'hodin'} není volný učitel.`,
          );
        } else {
          this.notify.success('Zástupy jsou doplněné, zkontrolujte je.');
        }
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('fill failed', err);
      },
    });
  }

  protected publish(): void {
    this.confirmation.confirm({
      header: 'Zveřejnit den',
      message: `Zveřejnit změny na ${this.dateLabel()}? Učitelé, žáci a rodiče, kterých se týkají, dostanou upozornění.`,
      icon: 'pi pi-send',
      acceptLabel: 'Zveřejnit',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(this.api.publish(this.dateOnly()), () =>
          this.notify.success('Změny dne jsou zveřejněné.'),
        ),
    });
  }
}
