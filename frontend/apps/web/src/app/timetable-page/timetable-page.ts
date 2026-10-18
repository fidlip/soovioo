import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import {
  AuthApiService,
  Classroom,
  ClassroomsApiService,
  DayOfWeek,
  SchoolClass,
  SchoolClassesApiService,
  SchoolPeriod,
  SchoolPeriodsApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  Teacher,
  TeachersApiService,
  TimetableApiService,
  WeekParity,
} from '@soovioo/shared';

const DAYS: DayOfWeek[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
];
const DAY_LABELS: Record<DayOfWeek, string> = {
  Monday: 'Pondělí',
  Tuesday: 'Úterý',
  Wednesday: 'Středa',
  Thursday: 'Čtvrtek',
  Friday: 'Pátek',
  Saturday: 'Sobota',
  Sunday: 'Neděle',
};

const DAY_OPTIONS = DAYS.map((value) => ({ value, label: DAY_LABELS[value] }));

const WEEK_PARITY_OPTIONS: { label: string; value: WeekParity }[] = [
  { label: 'Každý týden', value: 'Every' },
  { label: 'Lichý týden', value: 'Odd' },
  { label: 'Sudý týden', value: 'Even' },
];

type ViewMode = 'class' | 'teacher' | 'student';

/** Common shape both `getTimetable` (class-scoped) and `getTeacherTimetable`
 * (spans several classes) are normalized into — `schoolClassName` is only
 * ever populated for the latter, since a class view already knows which
 * class it's showing and doesn't need to repeat it per cell. */
interface GridEntry {
  id: number;
  schoolClassId: number;
  schoolClassName: string | null;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
  teacherId: number | null;
  teacherName: string | null;
  classroomId: number | null;
  classroomName: string | null;
}

/** "HH:mm:ss" (backend TimeOnly) -> "HH:mm" (<input type="time">). */
function toInputTime(value: string): string {
  return value.slice(0, 5);
}

/** p-datePicker's ngModel needs a Date even in timeOnly mode — only the
 * hours/minutes are read back out via dateToTimeString(). */
function timeStringToDate(hhmm: string): Date {
  const [hours, minutes] = hhmm.split(':').map(Number);
  const date = new Date();
  date.setHours(hours, minutes, 0, 0);
  return date;
}

function dateToTimeString(date: Date): string {
  const hours = `${date.getHours()}`.padStart(2, '0');
  const minutes = `${date.getMinutes()}`.padStart(2, '0');
  return `${hours}:${minutes}`;
}

function mondayOf(date: Date): Date {
  const d = new Date(date);
  const day = d.getDay();
  d.setDate(d.getDate() + (day === 0 ? -6 : 1) - day);
  d.setHours(0, 0, 0, 0);
  return d;
}

/** ISO 8601 week number — the "lichý/sudý týden" a TimetableEntry's
 * WeekParity refers to has no other definition anywhere in this app yet
 * (this page is the first thing that actually resolves "which week is
 * this"), so this is the standard, unambiguous choice. */
function isoWeekNumber(date: Date): number {
  const d = new Date(
    Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()),
  );
  const dayNum = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7);
}

function formatShortDate(date: Date): string {
  return date.toLocaleDateString('cs-CZ', { day: 'numeric', month: 'numeric' });
}

@Component({
  selector: 'app-timetable-page',
  imports: [
    FormsModule,
    Button,
    Card,
    DatePicker,
    Dialog,
    InputText,
    Message,
    Select,
  ],
  templateUrl: './timetable-page.html',
})
export class TimetablePage {
  private readonly timetableApi = inject(TimetableApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly teachersApi = inject(TeachersApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolPeriodsApi = inject(SchoolPeriodsApiService);
  protected readonly authApi = inject(AuthApiService);

  protected readonly days = DAYS;
  protected readonly dayLabels = DAY_LABELS;
  protected readonly dayOptions = DAY_OPTIONS;
  protected readonly weekParityOptions = WEEK_PARITY_OPTIONS;

  protected readonly subjects = signal<Subject[]>([]);
  protected readonly classrooms = signal<Classroom[]>([]);
  protected readonly teachers = signal<Teacher[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly periods = signal<SchoolPeriod[]>([]);
  protected readonly entries = signal<GridEntry[]>([]);

  /** Only Teacher/Admin ever see this — Parent picks among their own
   * children directly (no "mode"), Student gets no picker at all. */
  protected readonly viewMode = signal<ViewMode>('class');
  protected readonly selectedClassId = signal<number | null>(null);
  protected readonly selectedTeacherId = signal<number | null>(null);
  protected readonly selectedStudentId = signal<number | null>(null);

  protected readonly weekStart = signal(mondayOf(new Date()));
  protected readonly editMode = signal(false);

  protected readonly subjectsById = computed(
    () => new Map(this.subjects().map((s) => [s.id, s])),
  );
  protected readonly classroomOptions = computed(() =>
    this.classrooms().map((c) => ({ id: c.id, label: c.name })),
  );

  protected readonly viewModeOptions = computed(() => {
    if (this.authApi.isAdmin()) {
      return [
        { label: 'Třída', value: 'class' as ViewMode },
        { label: 'Učitel', value: 'teacher' as ViewMode },
      ];
    }
    return [
      { label: 'Učitel', value: 'teacher' as ViewMode },
      { label: 'Třída', value: 'class' as ViewMode },
      { label: 'Žák', value: 'student' as ViewMode },
    ];
  });

  protected readonly classOptions = computed(() =>
    this.schoolClasses().map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly teacherOptions = computed(() =>
    this.teachers().map((t) => ({ id: t.id, label: t.username })),
  );
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
    })),
  );

  /** Left corner cell: which class/teacher/student is being shown, plus the
   * displayed week's date range. */
  protected readonly cornerLabel = computed(() => {
    const mode = this.viewMode();
    if (mode === 'class') {
      return (
        this.schoolClasses().find((c) => c.id === this.selectedClassId())
          ?.name ?? ''
      );
    }
    if (mode === 'teacher') {
      return (
        this.teachers().find((t) => t.id === this.selectedTeacherId())
          ?.username ?? ''
      );
    }
    const student = this.students().find(
      (s) => s.id === this.selectedStudentId(),
    );
    return student
      ? `${student.firstName} ${student.lastName} (${student.schoolClassName})`
      : '';
  });

  protected readonly weekLabel = computed(() => {
    const start = this.weekStart();
    const end = new Date(start);
    end.setDate(end.getDate() + 4);
    return `${formatShortDate(start)}–${formatShortDate(end)} (týden ${isoWeekNumber(start)})`;
  });

  protected readonly currentParity = computed<'Odd' | 'Even'>(() =>
    isoWeekNumber(this.weekStart()) % 2 === 1 ? 'Odd' : 'Even',
  );

  protected readonly periodsByNumber = computed(
    () => new Map(this.periods().map((p) => [p.periodNumber, p])),
  );

  protected readonly periodOptions = computed(() =>
    this.periods().map((p) => ({
      value: p.periodNumber,
      label: `${p.periodNumber}. (${toInputTime(p.startTime)}–${toInputTime(p.endTime)})`,
    })),
  );

  /** Entries visible for the currently displayed week — "Every" always
   * shows, "Odd"/"Even" only on a matching week. */
  protected readonly visibleEntries = computed(() =>
    this.entries().filter(
      (e) => e.weekParity === 'Every' || e.weekParity === this.currentParity(),
    ),
  );

  protected readonly entryGrid = computed(() => {
    const grid = new Map<DayOfWeek, Map<number, GridEntry>>();
    for (const day of this.days) {
      grid.set(day, new Map());
    }
    for (const entry of this.visibleEntries()) {
      grid.get(entry.dayOfWeek)?.set(entry.periodNumber, entry);
    }
    return grid;
  });

  // Dialog state — add (no editingEntryId) or edit (editingEntryId set) a
  // single TimetableEntry, triggered by clicking a grid cell.
  protected readonly dialogVisible = signal(false);
  protected readonly editingEntryId = signal<number | null>(null);
  protected readonly editingEntrySchoolClassId = signal<number | null>(null);
  protected formSubjectId: number | null = null;
  protected formDayOfWeek: DayOfWeek = 'Monday';
  protected formPeriodNumber = 1;
  protected formWeekParity: WeekParity = 'Every';
  protected formTeacherId: number | null = null;
  protected formClassroomId: number | null = null;
  /** True once the user has picked a classroom by hand in the currently
   * open dialog — stops onFormSubjectChange() from clobbering it. */
  private formClassroomTouched = false;
  /** Non-blocking warning from the last save — shown above the grid since
   * the dialog closes on success, see saveEntry(). */
  protected readonly classroomConflictWarning = signal<string | null>(null);

  protected newPeriodNumberToConfigure = 1;
  protected newPeriodStartTime = timeStringToDate('08:00');
  protected newPeriodEndTime = timeStringToDate('08:45');
  protected editingPeriodNumber: number | null = null;
  protected editPeriodStartTime: Date | null = null;
  protected editPeriodEndTime: Date | null = null;

  constructor() {
    this.schoolPeriodsApi.getPeriods().subscribe({
      next: (periods) => this.periods.set(periods),
      error: (err) => console.error('getPeriods failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.formSubjectId ??= subjects[0]?.id ?? null;
      },
      error: (err) => console.error('getSubjects failed', err),
    });
    this.classroomsApi.getClassrooms().subscribe({
      next: (classrooms) => this.classrooms.set(classrooms),
      error: (err) => console.error('getClassrooms failed', err),
    });

    if (this.authApi.isAdmin()) {
      this.viewMode.set('class');
      this.loadSchoolClasses(true);
      this.loadTeachers(false);
    } else if (this.authApi.isTeacher()) {
      this.viewMode.set('teacher');
      this.loadTeachers(true);
      this.loadSchoolClasses(false);
      this.studentsApi.getStudents().subscribe({
        next: (students) => this.students.set(students),
        error: (err) => console.error('getStudents failed', err),
      });
    } else {
      // Parent/Student — no mode picker, just their own child(ren)/self.
      this.viewMode.set('student');
      this.studentsApi.getStudents().subscribe({
        next: (students) => {
          this.students.set(students);
          this.selectedStudentId.set(students[0]?.id ?? null);
          this.reload();
        },
        error: (err) => console.error('getStudents failed', err),
      });
    }
  }

  private loadSchoolClasses(selectFirst: boolean): void {
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (classes) => {
        this.schoolClasses.set(classes);
        if (selectFirst) {
          this.selectedClassId.set(classes[0]?.id ?? null);
          this.reload();
        }
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
  }

  private loadTeachers(selectSelf: boolean): void {
    this.teachersApi.getTeachers().subscribe({
      next: (teachers) => {
        this.teachers.set(teachers);
        if (selectSelf) {
          const me = teachers.find(
            (t) => t.username === this.authApi.getUsername(),
          );
          this.selectedTeacherId.set(me?.id ?? teachers[0]?.id ?? null);
          this.reload();
        }
      },
      error: (err) => console.error('getTeachers failed', err),
    });
  }

  protected canManageTimetable(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  /** Adding a brand new entry only makes sense picking a single class to
   * add it to — teacher/student views span (or resolve to) a class
   * implicitly, but offering "add" there would leave it ambiguous which
   * class a newly clicked empty cell belongs to. Editing/deleting an
   * existing entry works in every mode (see onCellClick). */
  protected canAddEntries(): boolean {
    return this.canManageTimetable() && this.viewMode() === 'class';
  }

  protected onViewModeChange(mode: ViewMode): void {
    this.viewMode.set(mode);

    // Each list may already be loaded (e.g. the class list, fetched once up
    // front to populate the "Třída" picker option set for a Teacher without
    // selecting one) without a *selection* having been made yet — so the
    // condition to auto-pick the first option is "nothing selected", not
    // "list not loaded".
    if (mode === 'class' && this.selectedClassId() === null) {
      this.loadSchoolClasses(true);
      return;
    }
    if (mode === 'teacher' && this.selectedTeacherId() === null) {
      this.loadTeachers(true);
      return;
    }
    if (mode === 'student' && this.selectedStudentId() === null) {
      this.studentsApi.getStudents().subscribe({
        next: (students) => {
          this.students.set(students);
          this.selectedStudentId.set(students[0]?.id ?? null);
          this.reload();
        },
        error: (err) => console.error('getStudents failed', err),
      });
      return;
    }
    this.reload();
  }

  protected onClassSelected(id: number): void {
    this.selectedClassId.set(id);
    this.reload();
  }

  protected onTeacherSelected(id: number): void {
    this.selectedTeacherId.set(id);
    this.reload();
  }

  protected onStudentSelected(id: number): void {
    this.selectedStudentId.set(id);
    this.reload();
  }

  protected previousWeek(): void {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() - 7);
    this.weekStart.set(d);
  }

  protected nextWeek(): void {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() + 7);
    this.weekStart.set(d);
  }

  protected thisWeek(): void {
    this.weekStart.set(mondayOf(new Date()));
  }

  protected periodTimeLabel(periodNumber: number): string {
    const period = this.periodsByNumber().get(periodNumber);
    return period
      ? `${toInputTime(period.startTime)}–${toInputTime(period.endTime)}`
      : '—';
  }

  protected cellTitle(entry: GridEntry): string {
    const parity =
      entry.weekParity === 'Every'
        ? ''
        : entry.weekParity === 'Odd'
          ? ' (lichý týden)'
          : ' (sudý týden)';
    const className = entry.schoolClassName
      ? ` — ${entry.schoolClassName}`
      : '';
    const classroom = entry.classroomName ? ` — ${entry.classroomName}` : '';
    return `${entry.subjectName}${className}${entry.teacherName ? ' — ' + entry.teacherName : ''}${classroom}${parity}`;
  }

  protected onCellClick(day: DayOfWeek, periodNumber: number): void {
    if (!this.editMode() || !this.canManageTimetable()) {
      return;
    }
    const entry = this.entryGrid().get(day)?.get(periodNumber);
    if (entry) {
      this.openEditDialog(entry);
    } else if (this.canAddEntries()) {
      this.openAddDialog(day, periodNumber);
    }
  }

  private openAddDialog(day: DayOfWeek, periodNumber: number): void {
    this.editingEntryId.set(null);
    this.editingEntrySchoolClassId.set(this.selectedClassId());
    this.formSubjectId = this.subjects()[0]?.id ?? null;
    this.formDayOfWeek = day;
    this.formPeriodNumber = periodNumber;
    this.formWeekParity = 'Every';
    this.formTeacherId = null;
    this.formClassroomId =
      this.subjectsById().get(this.formSubjectId ?? -1)?.defaultClassroomId ??
      null;
    this.formClassroomTouched = false;
    this.dialogVisible.set(true);
  }

  private openEditDialog(entry: GridEntry): void {
    this.editingEntryId.set(entry.id);
    this.editingEntrySchoolClassId.set(entry.schoolClassId);
    this.formSubjectId = entry.subjectId;
    this.formDayOfWeek = entry.dayOfWeek;
    this.formPeriodNumber = entry.periodNumber;
    this.formWeekParity = entry.weekParity;
    this.formTeacherId = entry.teacherId;
    this.formClassroomId = entry.classroomId;
    // Editing already has a real classroom (or an intentional "none") —
    // don't let a later subject change silently overwrite it.
    this.formClassroomTouched = true;
    this.dialogVisible.set(true);
  }

  protected closeDialog(): void {
    this.dialogVisible.set(false);
  }

  /** Pre-fills the classroom from the newly picked subject's own default —
   * still freely overridable right after, never re-applied once the user
   * has touched the classroom field themselves (see formClassroomTouched). */
  protected onFormSubjectChange(): void {
    if (this.formClassroomTouched) {
      return;
    }
    this.formClassroomId =
      this.subjectsById().get(this.formSubjectId ?? -1)?.defaultClassroomId ??
      null;
  }

  protected onFormClassroomChange(): void {
    this.formClassroomTouched = true;
  }

  protected saveEntry(): void {
    const schoolClassId = this.editingEntrySchoolClassId();
    if (!schoolClassId || !this.formSubjectId) {
      return;
    }
    const request = {
      subjectId: this.formSubjectId,
      dayOfWeek: this.formDayOfWeek,
      periodNumber: this.formPeriodNumber,
      weekParity: this.formWeekParity,
      teacherId: this.formTeacherId,
      classroomId: this.formClassroomId,
    };
    const editingId = this.editingEntryId();
    const result =
      editingId === null
        ? this.timetableApi.addEntry(schoolClassId, request)
        : this.timetableApi.updateEntry(schoolClassId, editingId, request);
    result.subscribe({
      next: (saved) => {
        this.dialogVisible.set(false);
        this.classroomConflictWarning.set(saved.classroomConflict);
        this.reload();
      },
      error: (err) => console.error('saveEntry failed', err),
    });
  }

  protected deleteEntry(): void {
    const schoolClassId = this.editingEntrySchoolClassId();
    const editingId = this.editingEntryId();
    if (!schoolClassId || editingId === null) {
      return;
    }
    this.timetableApi.deleteEntry(schoolClassId, editingId).subscribe({
      next: () => {
        this.dialogVisible.set(false);
        this.reload();
      },
      error: (err) => console.error('deleteEntry failed', err),
    });
  }

  protected addPeriod(): void {
    this.schoolPeriodsApi
      .upsertPeriod(this.newPeriodNumberToConfigure, {
        startTime: this.toBackendTime(
          dateToTimeString(this.newPeriodStartTime),
        ),
        endTime: this.toBackendTime(dateToTimeString(this.newPeriodEndTime)),
      })
      .subscribe({
        next: () => this.reloadPeriods(),
        error: (err) => console.error('upsertPeriod failed', err),
      });
  }

  protected startEditPeriod(period: SchoolPeriod): void {
    this.editingPeriodNumber = period.periodNumber;
    this.editPeriodStartTime = timeStringToDate(toInputTime(period.startTime));
    this.editPeriodEndTime = timeStringToDate(toInputTime(period.endTime));
  }

  protected cancelEditPeriod(): void {
    this.editingPeriodNumber = null;
  }

  protected saveEditPeriod(): void {
    if (
      this.editingPeriodNumber === null ||
      !this.editPeriodStartTime ||
      !this.editPeriodEndTime
    ) {
      return;
    }
    this.schoolPeriodsApi
      .upsertPeriod(this.editingPeriodNumber, {
        startTime: this.toBackendTime(
          dateToTimeString(this.editPeriodStartTime),
        ),
        endTime: this.toBackendTime(dateToTimeString(this.editPeriodEndTime)),
      })
      .subscribe({
        next: () => {
          this.editingPeriodNumber = null;
          this.reloadPeriods();
        },
        error: (err) => console.error('upsertPeriod failed', err),
      });
  }

  protected deletePeriod(period: SchoolPeriod): void {
    this.schoolPeriodsApi.deletePeriod(period.periodNumber).subscribe({
      next: () => this.reloadPeriods(),
      error: (err) => console.error('deletePeriod failed', err),
    });
  }

  private toBackendTime(value: string): string {
    return value.length === 5 ? `${value}:00` : value;
  }

  private reload(): void {
    const mode = this.viewMode();
    if (mode === 'class') {
      const classId = this.selectedClassId();
      if (!classId) {
        this.entries.set([]);
        return;
      }
      this.timetableApi.getTimetable(classId).subscribe({
        next: (entries) =>
          this.entries.set(
            entries.map((e) => ({ ...e, schoolClassName: null })),
          ),
        error: (err) => console.error('getTimetable failed', err),
      });
    } else if (mode === 'teacher') {
      const teacherId = this.selectedTeacherId();
      if (!teacherId) {
        this.entries.set([]);
        return;
      }
      this.timetableApi.getTeacherTimetable(teacherId).subscribe({
        next: (entries) => this.entries.set(entries),
        error: (err) => console.error('getTeacherTimetable failed', err),
      });
    } else {
      const student = this.students().find(
        (s) => s.id === this.selectedStudentId(),
      );
      if (!student) {
        this.entries.set([]);
        return;
      }
      this.timetableApi.getTimetable(student.schoolClassId).subscribe({
        next: (entries) =>
          this.entries.set(
            entries.map((e) => ({ ...e, schoolClassName: null })),
          ),
        error: (err) => console.error('getTimetable failed', err),
      });
    }
  }

  private reloadPeriods(): void {
    this.schoolPeriodsApi.getPeriods().subscribe({
      next: (periods) => this.periods.set(periods),
      error: (err) => console.error('getPeriods failed', err),
    });
  }
}
