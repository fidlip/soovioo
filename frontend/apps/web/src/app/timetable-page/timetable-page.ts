import {
  ChangeDetectorRef,
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
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
  StudentGroup,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  Teacher,
  TeacherTeaching,
  TeachersApiService,
  TeachingApiService,
  TimetableApiService,
  TimetableVersion,
  TimetableVersionsApiService,
  WeekParity,
  formatDate,
  formatHours,
  qualifiedFirst,
  qualifiedFor,
  taughtSummary,
  isoWeekNumber,
  localizeStudentTerm,
  timetableVersionOn,
  subjectClassroomIds,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS, FloatField } from '../forms';
import { toDateOnly } from '../departures/retention-labels';
import { Loading } from '../loading/loading';
import { TimetableGenerateDialog } from './timetable-generate-dialog';
import {
  TimetableVersionsDialog,
  versionStatusLabel,
  versionValidity,
} from './timetable-versions-dialog';

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

const DAY_SHORT_LABELS: Record<DayOfWeek, string> = {
  Monday: 'Po',
  Tuesday: 'Út',
  Wednesday: 'St',
  Thursday: 'Čt',
  Friday: 'Pá',
  Saturday: 'So',
  Sunday: 'Ne',
};

/** Fixed grid geometry — header and body are separate tables (see the
 * template), so both must lay their columns out identically. */
const DAY_COLUMN_WIDTH_REM = 3;
const PERIOD_COLUMN_MIN_WIDTH_REM = 7;

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
  conflicts: string[];
  locked: boolean;
  studentGroupId?: number | null;
  studentGroupName?: string | null;
}

/** "HH:mm:ss" (backend TimeOnly) -> "HH:mm" (<input type="time">). */
function toInputTime(value: string): string {
  return value.slice(0, 5);
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
function formatShortDate(date: Date): string {
  return formatDate(toDateOnly(date), 'dayMonth');
}

@Component({
  selector: 'app-timetable-page',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    Loading,
    TermPipe,
    AppIcon,
    FormsModule,
    Button,
    Checkbox,
    Dialog,
    Message,
    Select,
    TimetableGenerateDialog,
    TimetableVersionsDialog,
  ],
  templateUrl: './timetable-page.html',
})
export class TimetablePage {
  protected readonly notify = inject(NotifyService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly timetableApi = inject(TimetableApiService);
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly changeDetector = inject(ChangeDetectorRef);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly teachersApi = inject(TeachersApiService);
  private readonly teachingApi = inject(TeachingApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolPeriodsApi = inject(SchoolPeriodsApiService);
  protected readonly authApi = inject(AuthApiService);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);

  // Optional: the grid isn't rendered until gridReady().
  private readonly headerScroller =
    viewChild<ElementRef<HTMLElement>>('headerScroller');
  private readonly bodyScroller =
    viewChild<ElementRef<HTMLElement>>('bodyScroller');

  protected readonly days = DAYS;
  protected readonly dayLabels = DAY_LABELS;
  protected readonly dayShortLabels = DAY_SHORT_LABELS;
  protected readonly dayColumnWidth = `${DAY_COLUMN_WIDTH_REM}rem`;
  protected readonly dayOptions = DAY_OPTIONS;
  protected readonly weekParityOptions = WEEK_PARITY_OPTIONS;

  protected readonly subjects = signal<Subject[]>([]);
  /** The edited lesson's subject, kept on offer even when it's a row of
   * the plan divisions teach. */
  private readonly editedSubjectId = signal<number | null>(null);
  /** Not a row of the plan divisions teach — their groups have the lessons. */
  protected readonly lessonSubjects = computed(() =>
    this.subjects().filter(
      (s) => !s.coveredByDivisions || s.id === this.editedSubjectId(),
    ),
  );
  protected readonly classrooms = signal<Classroom[]>([]);
  protected readonly teachers = signal<Teacher[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly periods = signal<SchoolPeriod[]>([]);
  protected readonly entries = signal<GridEntry[]>([]);
  /** The grid (and the conflict banner) render only once both the bell
   * schedule and the first week's entries are in — building it up piece by
   * piece shifted the whole page around while it loaded. */
  private readonly periodsLoaded = signal(false);
  private readonly entriesLoaded = signal(false);
  protected readonly gridReady = computed(
    () => this.periodsLoaded() && this.entriesLoaded(),
  );

  /** Only Teacher/Admin ever see this — Parent picks among their own
   * children directly (no "mode"), Student gets no picker at all. */
  protected readonly viewMode = signal<ViewMode>('class');
  protected readonly selectedClassId = signal<number | null>(null);
  protected readonly selectedTeacherId = signal<number | null>(null);
  protected readonly selectedStudentId = signal<number | null>(null);

  protected readonly weekStart = signal(mondayOf(new Date()));
  protected readonly editMode = signal(false);

  /** Published ones for everyone; drafts and archived ones too for whoever
   * may edit the timetable. */
  protected readonly versions = signal<TimetableVersion[]>([]);
  /** null = the published version valid in the displayed week. */
  protected readonly selectedVersionId = signal<number | null>(null);
  protected readonly versionsDialogVisible = signal(false);
  /** The version the entries were last loaded for — undefined while the
   * list of versions wasn't in yet. */
  private loadedVersionId: number | null | undefined;

  private readonly weekVersion = computed(() =>
    timetableVersionOn(this.versions(), toDateOnly(this.weekStart())),
  );
  protected readonly shownVersion = computed(() => {
    const id = this.selectedVersionId();
    return id === null
      ? this.weekVersion()
      : this.versions().find((v) => v.id === id);
  });
  protected readonly versionOptions = computed(() => [
    { label: 'Podle týdne', value: null as number | null },
    ...this.versions().map((v) => ({
      label: `${v.name} (${versionStatusLabel(v, toDateOnly(new Date())).label.toLowerCase()})`,
      value: v.id as number | null,
    })),
  ]);
  /** Which version is on screen — shown once there's more than one to tell
   * apart, or always to whoever sees the drafts. */
  protected readonly versionInfo = computed(() => {
    const version = this.shownVersion();
    const several =
      this.versions().filter((v) => v.status === 'Published').length > 1;
    if (!version || (!several && !this.canSeeVersions())) {
      return null;
    }
    return {
      name: version.name,
      validity: versionValidity(version),
      status: versionStatusLabel(version, toDateOnly(new Date())),
      draft: version.status !== 'Published',
    };
  });

  /** The shown teacher's přidělení výuky — for themselves, or for whoever
   * sees the whole school's. */
  protected readonly teaching = signal<TeacherTeaching | null>(null);
  protected readonly teachingSummary = computed(() => {
    const teacher = this.teaching()?.teacher;
    if (!teacher) {
      return null;
    }
    const hours =
      teacher.contractedHours === null
        ? `Přiděleno ${formatHours(teacher.assignedHours)}`
        : `Přiděleno ${formatHours(teacher.assignedHours)} z ${formatHours(teacher.contractedHours)} nasmlouvaných`;
    const taught = taughtSummary(teacher.taught);
    return taught ? `${hours}: ${taught}` : hours;
  });

  protected readonly subjectsById = computed(
    () => new Map(this.subjects().map((s) => [s.id, s])),
  );
  protected readonly classroomOptions = computed(() =>
    this.classrooms().map((c) => ({ id: c.id, label: c.name })),
  );

  protected readonly viewModeOptions = computed(() => {
    if (this.authApi.can('Timetable')) {
      return [
        { label: 'Třída', value: 'class' as ViewMode },
        { label: 'Učitel', value: 'teacher' as ViewMode },
      ];
    }
    return [
      { label: 'Učitel', value: 'teacher' as ViewMode },
      { label: 'Třída', value: 'class' as ViewMode },
      {
        label: localizeStudentTerm('Žák', this.authApi.getSchoolType()),
        value: 'student' as ViewMode,
      },
    ];
  });

  protected readonly classOptions = computed(() =>
    this.schoolClasses().map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly teacherOptions = computed(() =>
    this.teachers().map((t) => ({ id: t.id, label: t.name })),
  );
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
    })),
  );

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

  /** Periods with at least one entry this week — empty columns are hidden,
   * except while editing, where every period must stay clickable to add an
   * entry into it. */
  protected readonly displayedPeriods = computed(() => {
    if (this.editMode()) {
      return this.periods();
    }
    const used = new Set(this.visibleEntries().map((e) => e.periodNumber));
    return this.periods().filter((p) => used.has(p.periodNumber));
  });

  protected readonly tableMinWidth = computed(
    () =>
      `${DAY_COLUMN_WIDTH_REM + this.displayedPeriods().length * PERIOD_COLUMN_MIN_WIDTH_REM}rem`,
  );

  /** Lessons of a slot — several where a class is in groups. */
  protected readonly entryGrid = computed(() => {
    const grid = new Map<DayOfWeek, Map<number, GridEntry[]>>();
    for (const day of this.days) {
      grid.set(day, new Map());
    }
    for (const entry of this.visibleEntries()) {
      const slots = grid.get(entry.dayOfWeek);
      const cell = slots?.get(entry.periodNumber);
      if (cell) {
        cell.push(entry);
      } else {
        slots?.set(entry.periodNumber, [entry]);
      }
    }
    return grid;
  });

  /** The shown version's groups — for a group's lesson added by hand. */
  private readonly groups = signal<StudentGroup[]>([]);
  protected readonly formGroupOptions = computed(() => {
    const classId = this.editingEntrySchoolClassId();
    return [
      { id: null as number | null, label: 'Celá třída' },
      ...this.groups()
        .filter((g) => classId !== null && g.classIds.includes(classId))
        .map((g) => ({ id: g.id as number | null, label: g.name })),
    ];
  });
  protected formGroupId: number | null = null;

  // Dialog state — add (no editingEntryId) or edit (editingEntryId set) a
  // single TimetableEntry, triggered by clicking a grid cell.
  protected readonly dialogVisible = signal(false);
  protected readonly editingEntryId = signal<number | null>(null);
  protected readonly editingEntrySchoolClassId = signal<number | null>(null);
  protected formSubjectId: number | null = null;
  protected formDayOfWeek: DayOfWeek = 'Monday';
  protected formPeriodNumber = 1;
  protected formWeekParity: WeekParity = 'Every';
  protected formLocked = false;
  /** Of the lesson being edited — a group's lesson stays the group's. */
  protected editingGroupName: string | null = null;
  protected formTeacherId: number | null = null;
  protected formClassroomId: number | null = null;
  /** True once the user has picked a classroom by hand in the currently
   * open dialog — stops onFormSubjectChange() from clobbering it. */
  private formClassroomTouched = false;
  /** Teacher/classroom collisions in the displayed week, one line per
   * conflicting entry — shown in a banner above the grid (staff only). */
  protected readonly conflictLines = computed(() =>
    this.canManageTimetable()
      ? this.visibleEntries()
          .filter((e) => e.conflicts.length > 0)
          .map(
            (e) =>
              `${DAY_LABELS[e.dayOfWeek]}, ${e.periodNumber}. hodina — ${e.subjectAbbreviation ?? e.subjectName}${e.schoolClassName ? ` (${e.schoolClassName})` : ''}: ${e.conflicts.join(' ')}`,
          )
      : [],
  );

  constructor() {
    this.loadVersions();
    const router = inject(Router);
    inject(ActivatedRoute)
      .queryParamMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        const version = Number(params.get('version'));
        if (!version || params.get('generation') !== 'result') {
          return;
        }
        router.navigate([], { queryParams: {}, replaceUrl: true });
        this.loadVersions(() => {
          this.onVersionSelected(version);
          this.generateShowResult.set(true);
          this.generateDialogVisible.set(true);
        });
      });
    this.schoolPeriodsApi.getPeriods().subscribe({
      next: (periods) => {
        this.periods.set(periods);
        this.periodsLoaded.set(true);
        // Entries were faster: the grid renders only now.
        if (this.entriesLoaded()) {
          this.scrollToCurrentPeriodAfterRender();
        }
      },
      error: (err) => {
        console.error('getPeriods failed', err);
        this.periodsLoaded.set(true);
      },
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.formSubjectId ??= this.lessonSubjects()[0]?.id ?? null;
      },
      error: (err) => console.error('getSubjects failed', err),
    });
    this.classroomsApi.getClassrooms().subscribe({
      next: (classrooms) => this.classrooms.set(classrooms),
      error: (err) => console.error('getClassrooms failed', err),
    });

    if (this.authApi.can('Timetable')) {
      this.viewMode.set('class');
      this.loadSchoolClasses(true);
      this.loadTeachers(false);
    } else if (this.authApi.isStaff()) {
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
    return this.authApi.can('Timetable');
  }

  /** Drafts and archived versions — to edit, or only to look at. */
  protected canSeeVersions(): boolean {
    return (
      this.canManageTimetable() || this.authApi.can('TimetableVersionsRead')
    );
  }

  protected readonly generateDialogVisible = signal(false);
  /** Opened from the "generated" notification: straight on the result. */
  protected readonly generateShowResult = signal(false);

  protected canGenerate(): boolean {
    return this.canManageTimetable() && this.shownVersion()?.status === 'Draft';
  }

  /** An archived version is only to look at. */
  protected canEditShownVersion(): boolean {
    return (
      this.canManageTimetable() && this.shownVersion()?.status !== 'Archived'
    );
  }

  private loadVersions(then?: () => void): void {
    this.versionsApi.getVersions().subscribe({
      next: (versions) => {
        this.versions.set(versions);
        if (!this.canEditShownVersion()) {
          this.editMode.set(false);
        }
        then?.();
      },
      error: (err) => console.error('getVersions failed', err),
    });
  }

  protected onVersionSelected(id: number | null): void {
    this.selectedVersionId.set(id);
    if (!this.canEditShownVersion()) {
      this.editMode.set(false);
    }
    this.reload();
  }

  protected onShowVersion(id: number): void {
    this.versionsDialogVisible.set(false);
    this.onVersionSelected(id);
  }

  /** Publishing or archiving moves where the other versions end. */
  protected onVersionsChanged(): void {
    this.loadVersions(() => this.reload());
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
    this.reloadIfVersionChanged();
  }

  protected nextWeek(): void {
    const d = new Date(this.weekStart());
    d.setDate(d.getDate() + 7);
    this.weekStart.set(d);
    this.reloadIfVersionChanged();
  }

  protected thisWeek(): void {
    this.weekStart.set(mondayOf(new Date()));
    this.reloadIfVersionChanged();
    this.scrollToCurrentPeriodAfterRender();
  }

  /** Another week may fall into another version of the timetable. */
  private reloadIfVersionChanged(): void {
    if (
      this.selectedVersionId() === null &&
      this.loadedVersionId !== (this.weekVersion()?.id ?? null)
    ) {
      this.reload();
    }
  }

  protected syncHeaderScroll(): void {
    const header = this.headerScroller()?.nativeElement;
    const body = this.bodyScroller()?.nativeElement;
    if (header && body) {
      header.scrollLeft = body.scrollLeft;
    }
  }

  /** On the current week, scrolls the grid horizontally so the period that's
   * running now (or the next one today) is the first column right of the
   * sticky day column — on a phone the morning periods would otherwise push
   * it off screen. No-op on other weeks, weekends and after the last period. */
  private scrollToCurrentPeriodAfterRender(): void {
    // A timetable request may answer after the page was left (NG0911).
    if (this.destroyRef.destroyed) {
      return;
    }
    afterNextRender(
      () => {
        const now = new Date();
        const day = now.getDay();
        if (
          day === 0 ||
          day === 6 ||
          mondayOf(now).getTime() !== this.weekStart().getTime()
        ) {
          return;
        }
        const nowTime = dateToTimeString(now);
        const current = this.displayedPeriods().find(
          (p) => toInputTime(p.endTime) > nowTime,
        );
        if (!current) {
          return;
        }
        const body = this.bodyScroller()?.nativeElement;
        if (!body) {
          return;
        }
        const cell = body.querySelector<HTMLElement>(
          `td[data-period="${current.periodNumber}"]`,
        );
        const dayColumn = body.querySelector<HTMLElement>('tbody th');
        if (!cell || !dayColumn) {
          return;
        }
        body.scrollLeft = cell.offsetLeft - dayColumn.offsetWidth;
      },
      { injector: this.injector },
    );
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
      : entry.studentGroupName
        ? ` — ${entry.studentGroupName}`
        : '';
    const classroom = entry.classroomName ? ` — ${entry.classroomName}` : '';
    const conflicts = this.canManageTimetable()
      ? entry.conflicts.map((c) => `\n⚠ ${c}`).join('')
      : '';
    return `${entry.subjectName}${className}${entry.teacherName ? ' — ' + entry.teacherName : ''}${classroom}${parity}${conflicts}`;
  }

  /** An empty slot: a new lesson; one lesson: that one. */
  protected onCellClick(day: DayOfWeek, periodNumber: number): void {
    if (!this.editMode() || !this.canManageTimetable()) {
      return;
    }
    const entries = this.entryGrid().get(day)?.get(periodNumber) ?? [];
    if (entries.length === 1) {
      this.openEditDialog(entries[0]);
    } else if (entries.length === 0 && this.canAddEntries()) {
      this.openAddDialog(day, periodNumber);
    }
  }

  protected onEntryClick(entry: GridEntry): void {
    if (this.editMode() && this.canManageTimetable()) {
      this.openEditDialog(entry);
    }
  }

  /** Another group's lesson where the class has a group's already. */
  protected canAddGroupLesson(entries: GridEntry[]): boolean {
    return (
      this.canAddEntries() &&
      entries.length > 0 &&
      entries.every((e) => e.studentGroupId) &&
      this.formGroupOptions().length > 1
    );
  }

  protected addToCell(day: DayOfWeek, periodNumber: number): void {
    this.openAddDialog(day, periodNumber);
  }

  /** A group's lesson has its subject. */
  protected onFormGroupChange(): void {
    const group = this.groups().find((g) => g.id === this.formGroupId);
    if (!group) {
      return;
    }
    this.formSubjectId = group.subjectId;
    this.formTeacherId = group.teacherId ?? this.formTeacherId;
    if (group.classroomId !== null) {
      this.formClassroomId = group.classroomId;
      this.formClassroomTouched = true;
    } else {
      this.prefillClassroom();
    }
  }

  private loadGroups(): void {
    const versionId = this.shownVersion()?.id;
    const allowed =
      this.authApi.can('Teaching') ||
      this.authApi.scope('TeachingRead') === 'School';
    if (versionId === undefined || !allowed || !this.canManageTimetable()) {
      this.groups.set([]);
      return;
    }
    this.teachingApi.getGroups(versionId).subscribe({
      next: (data) => this.groups.set(data.groups),
      error: (err) => console.error('getGroups failed', err),
    });
  }

  /** Who has the aprobace for the subject in the class first. */
  protected formTeacherOptions(): { id: number; label: string }[] {
    const subjectId = this.formSubjectId;
    const grade =
      this.schoolClasses().find(
        (c) => c.id === this.editingEntrySchoolClassId(),
      )?.rocnik ?? null;
    return qualifiedFirst(
      this.teachers().map((t) => ({
        id: t.id,
        label: t.name,
        qualifications: t.qualifications,
      })),
      (o) =>
        subjectId !== null && qualifiedFor(o.qualifications, subjectId, grade),
    );
  }

  private openAddDialog(day: DayOfWeek, periodNumber: number): void {
    this.editingEntryId.set(null);
    this.formGroupId = null;
    this.editingGroupName = null;
    this.loadGroups();
    this.editingEntrySchoolClassId.set(this.selectedClassId());
    this.editedSubjectId.set(null);
    this.formSubjectId = this.lessonSubjects()[0]?.id ?? null;
    this.formDayOfWeek = day;
    this.formPeriodNumber = periodNumber;
    this.formWeekParity = 'Every';
    this.formLocked = false;
    this.formTeacherId = null;
    this.formClassroomId = null;
    this.formClassroomTouched = false;
    this.prefillClassroom();
    this.notify.clear();
    this.dialogVisible.set(true);
  }

  private openEditDialog(entry: GridEntry): void {
    this.editingEntryId.set(entry.id);
    this.formGroupId = entry.studentGroupId ?? null;
    this.editingGroupName = entry.studentGroupName ?? null;
    this.editingEntrySchoolClassId.set(entry.schoolClassId);
    this.editedSubjectId.set(entry.subjectId);
    this.formSubjectId = entry.subjectId;
    this.formDayOfWeek = entry.dayOfWeek;
    this.formPeriodNumber = entry.periodNumber;
    this.formWeekParity = entry.weekParity;
    this.formLocked = entry.locked;
    this.formTeacherId = entry.teacherId;
    this.formClassroomId = entry.classroomId;
    // Editing already has a real classroom (or an intentional "none") —
    // don't let a later subject change silently overwrite it.
    this.formClassroomTouched = true;
    this.notify.clear();
    this.dialogVisible.set(true);
  }

  protected closeDialog(): void {
    this.dialogVisible.set(false);
  }

  /** Pre-fills the classroom from the newly picked subject's own default —
   * still freely overridable right after, never re-applied once the user
   * has touched the classroom field themselves (see formClassroomTouched). */
  protected onFormSubjectChange(): void {
    this.prefillClassroom();
  }

  /** The first free classroom at the form's slot: the subject's, or the
   * class's homeroom for a subject without a list. */
  protected prefillClassroom(): void {
    if (this.formClassroomTouched) {
      return;
    }
    const rooms = subjectClassroomIds(
      this.subjectsById().get(this.formSubjectId ?? -1),
      this.classrooms(),
      this.schoolClasses().find(
        (c) => c.id === this.editingEntrySchoolClassId(),
      )?.homeroomId ?? null,
    );
    const versionId = this.shownVersion()?.id;
    this.formClassroomId = rooms[0] ?? null;
    if (rooms.length === 0 || versionId === undefined) {
      return;
    }
    const slot = [
      this.formSubjectId,
      this.formDayOfWeek,
      this.formPeriodNumber,
      this.formWeekParity,
    ].join();
    this.versionsApi
      .getBusyClassrooms(
        versionId,
        this.formDayOfWeek,
        this.formPeriodNumber,
        this.formWeekParity,
      )
      .subscribe({
        next: (busy) => {
          const now = [
            this.formSubjectId,
            this.formDayOfWeek,
            this.formPeriodNumber,
            this.formWeekParity,
          ].join();
          if (this.formClassroomTouched || now !== slot) {
            return;
          }
          this.formClassroomId = rooms.find((r) => !busy.includes(r)) ?? null;
          this.changeDetector.markForCheck();
        },
      });
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
      timetableVersionId: this.shownVersion()?.id ?? null,
      locked: this.formLocked,
      studentGroupId: this.formGroupId,
    };
    const editingId = this.editingEntryId();
    const result =
      editingId === null
        ? this.timetableApi.addEntry(schoolClassId, request)
        : this.timetableApi.updateEntry(schoolClassId, editingId, request);
    result.subscribe({
      next: () => {
        this.dialogVisible.set(false);
        // The school's first lesson started its first version.
        if (this.versions().length === 0) {
          this.loadVersions(() => this.reload());
        } else {
          this.loadVersions();
          this.reload();
        }
      },
      error: (err) => this.notify.actionFailed('saveEntry failed', err),
    });
  }

  /** The cross on a cell in edit mode — asks first, the dialog isn't open. */
  protected confirmDeleteEntry(entry: GridEntry): void {
    this.confirmation.confirm({
      header: 'Smazat hodinu',
      message: `Smazat ${entry.subjectName} (${DAY_LABELS[entry.dayOfWeek]}, ${entry.periodNumber}. hodina)${entry.schoolClassName ? ` — ${entry.schoolClassName}` : entry.studentGroupName ? ` — ${entry.studentGroupName}` : ''}?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.timetableApi.deleteEntry(entry.schoolClassId, entry.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.notify.actionFailed('deleteEntry failed', err),
        }),
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
      error: (err) => this.notify.actionFailed('deleteEntry failed', err),
    });
  }

  private loadTeaching(teacherId: number): void {
    this.teaching.set(null);
    const schoolWide =
      this.authApi.can('Teaching') ||
      this.authApi.scope('TeachingRead') === 'School';
    if (!schoolWide && teacherId !== this.authApi.getUserId()) {
      return;
    }
    this.teachingApi
      .getTeacherTeaching(teacherId, this.loadedVersionId)
      .subscribe({
        next: (teaching) => {
          if (this.selectedTeacherId() === teacherId) {
            this.teaching.set(teaching);
          }
        },
        error: (err) => console.error('getTeacherTeaching failed', err),
      });
  }

  private reload(): void {
    const selected = this.selectedVersionId();
    const query =
      selected === null
        ? { date: toDateOnly(this.weekStart()) }
        : { versionId: selected };
    this.loadedVersionId =
      selected ??
      (this.versions().length > 0
        ? (this.weekVersion()?.id ?? null)
        : undefined);
    const mode = this.viewMode();
    if (mode === 'class') {
      const classId = this.selectedClassId();
      if (!classId) {
        this.entries.set([]);
        this.entriesLoaded.set(true);
        return;
      }
      this.timetableApi.getTimetable(classId, query).subscribe({
        next: (entries) =>
          this.setEntries(
            entries.map((e) => ({ ...e, schoolClassName: null })),
          ),
        error: (err) => {
          console.error('getTimetable failed', err);
          this.entriesLoaded.set(true);
        },
      });
    } else if (mode === 'teacher') {
      const teacherId = this.selectedTeacherId();
      if (!teacherId) {
        this.entries.set([]);
        this.entriesLoaded.set(true);
        return;
      }
      this.loadTeaching(teacherId);
      this.timetableApi.getTeacherTimetable(teacherId, query).subscribe({
        next: (entries) => this.setEntries(entries),
        error: (err) => {
          console.error('getTeacherTimetable failed', err);
          this.entriesLoaded.set(true);
        },
      });
    } else {
      const student = this.students().find(
        (s) => s.id === this.selectedStudentId(),
      );
      if (!student) {
        this.entries.set([]);
        this.entriesLoaded.set(true);
        return;
      }
      this.timetableApi
        .getTimetable(student.schoolClassId, {
          ...query,
          studentId: student.id,
        })
        .subscribe({
          next: (entries) =>
            this.setEntries(
              entries.map((e) => ({ ...e, schoolClassName: null })),
            ),
          error: (err) => {
            console.error('getTimetable failed', err);
            this.entriesLoaded.set(true);
          },
        });
    }
  }

  private setEntries(entries: GridEntry[]): void {
    this.entries.set(entries);
    this.entriesLoaded.set(true);
    this.scrollToCurrentPeriodAfterRender();
  }
}
