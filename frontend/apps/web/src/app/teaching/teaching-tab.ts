import {
  Component,
  computed,
  effect,
  inject,
  input,
  model,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  Tab,
  TabList,
  TabPanel,
  TabPanels,
  Tabs,
} from '@openng/optimus-ui/tabs';
import {
  AuthApiService,
  Subject,
  SubjectsApiService,
  Teacher,
  TeachersApiService,
  TeachingApiService,
  TeachingClass,
  TeachingOverview,
  TeachingRow,
  formatHours,
  qualifiedFor,
  taughtSummary,
  teachingDaysLabel,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';
import { PeriodsEditor } from '../periods-editor/periods-editor';
import { TeacherWishesDefaults } from '../periods-editor/teacher-wishes-defaults';
import { TimetablePage } from '../timetable-page/timetable-page';
import { TimetableVersionSelection } from '../timetable-page/timetable-version-selection';
import { RvpPanel } from './rvp-panel';
import { StudentGroupsPanel } from './student-groups';

const OVERVIEW_SECTIONS = ['classes', 'teachers', 'groups', 'plan'];

/** Administrace → Výuka: the one choice of a timetable version; that version's
 * timetable to edit; the učební plán of the version
 * (the grade's, changed for a class where it differs), who teaches what in
 * each class
 * (the system picks where the school didn't) and what doesn't add up. A
 * teacher who may see only their own gets their rows. */
@Component({
  selector: 'app-teaching-tab',
  imports: [
    Button,
    Checkbox,
    FloatField,
    FormsModule,
    InputNumber,
    Loading,
    Message,
    PeriodsEditor,
    RvpPanel,
    Select,
    StudentGroupsPanel,
    TableModule,
    Tab,
    TabList,
    TabPanel,
    TabPanels,
    Tabs,
    Tag,
    TeacherWishesDefaults,
    TimetablePage,
    Tooltip,
  ],
  providers: [TimetableVersionSelection],
  templateUrl: './teaching-tab.html',
})
export class TeachingTab {
  private readonly teachingApi = inject(TeachingApiService);
  protected readonly selection = inject(TimetableVersionSelection);
  private readonly teachersApi = inject(TeachersApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly notify = inject(NotifyService);
  protected readonly authApi = inject(AuthApiService);

  protected readonly formatHours = formatHours;
  protected readonly taughtSummary = taughtSummary;
  protected readonly days = teachingDaysLabel;

  /** Opens filtered to the class or teacher (from Třídy, Osoby). */
  readonly focus = input<{ classId?: number; teacherId?: number } | null>(null);

  /** The open section, kept by Administrace in the address. */
  readonly section = model<string | null>(null);

  private readonly canSeeOverview =
    this.authApi.can('TeachingRead') || this.authApi.can('Teaching');
  protected readonly sections = computed(() => [
    ...(this.authApi.can('Timetable') ||
    this.authApi.can('TimetableVersionsRead')
      ? [{ value: 'timetable', label: 'Rozvrh' }]
      : []),
    ...(this.canSeeOverview
      ? [
          { value: 'classes', label: 'Třídy' },
          { value: 'teachers', label: 'Učitelé' },
          { value: 'groups', label: 'Skupiny' },
          { value: 'plan', label: 'Učební plán' },
        ]
      : []),
    ...(this.authApi.can('Timetable')
      ? [{ value: 'settings', label: 'Nastavení' }]
      : []),
  ]);
  protected readonly activeSection = computed(() => {
    const sections = this.sections();
    const wanted = this.section();
    return (
      sections.find((s) => s.value === wanted)?.value ?? sections[0]?.value
    );
  });
  protected readonly showsOverview = computed(() =>
    OVERVIEW_SECTIONS.includes(this.activeSection()),
  );

  protected readonly versionId = this.selection.versionId;
  protected readonly overview = signal<TeachingOverview | null>(null);
  protected readonly loading = signal(this.canSeeOverview);
  protected readonly shownOverview = computed(() =>
    this.loading() ? null : this.overview(),
  );
  protected readonly busy = signal(false);
  protected readonly teachers = signal<Teacher[]>([]);
  protected readonly subjects = signal<Subject[]>([]);

  /** Hours being edited in Učební plán, by `grade-subjectId`. */
  protected plan: Record<string, number | null> = {};
  /** Blocks of two in Učební plán where the grade differs from the
   * subject, by `grade-subjectId`. */
  protected planBlocks: Record<string, boolean | null> = {};
  /** A class's hours being edited, by `classId-subjectId`. */
  protected classPlan: Record<string, number | null> = {};

  protected readonly classFilter = signal<number | null>(null);
  protected readonly teacherFilter = signal<number | null>(null);
  protected readonly subjectFilter = signal<number | null>(null);
  protected readonly problemsOnly = signal(false);

  protected readonly classFilterOptions = computed(() =>
    (this.overview()?.classes ?? []).map((c) => ({
      label: c.name,
      value: c.id,
    })),
  );
  protected readonly teacherFilterOptions = computed(() =>
    (this.overview()?.teachers ?? []).map((t) => ({
      label: t.name,
      value: t.id,
    })),
  );

  /** Classes and their rows the filters let through; a class keeps its
   * own problems only while no row filter is on. */
  protected readonly shownClasses = computed(() => {
    const classId = this.classFilter();
    const teacherId = this.teacherFilter();
    const subjectId = this.subjectFilter();
    const problemsOnly = this.problemsOnly();
    const rowFilter = teacherId !== null || subjectId !== null;
    return (this.overview()?.classes ?? [])
      .filter((c) => classId === null || c.id === classId)
      .map((c) => ({
        ...c,
        problems: rowFilter ? [] : c.problems,
        rows: c.rows.filter(
          (r) =>
            (teacherId === null || r.teacherId === teacherId) &&
            (subjectId === null || r.subjectId === subjectId) &&
            (!problemsOnly || r.problems.length > 0),
        ),
      }))
      .filter(
        (c) =>
          c.rows.length > 0 ||
          (!rowFilter && (!problemsOnly || c.problems.length > 0)) ||
          c.id === classId,
      );
  });

  protected readonly shownTeachers = computed(() => {
    const classId = this.classFilter();
    const teacherId = this.teacherFilter();
    const subjectId = this.subjectFilter();
    const problemsOnly = this.problemsOnly();
    const classes = this.overview()?.classes ?? [];
    const teaches = (id: number) =>
      classes.some(
        (c) =>
          (classId === null || c.id === classId) &&
          c.rows.some(
            (r) =>
              r.teacherId === id &&
              (subjectId === null || r.subjectId === subjectId),
          ),
      );
    return (this.overview()?.teachers ?? []).filter(
      (t) =>
        (teacherId === null || t.id === teacherId) &&
        ((classId === null && subjectId === null) || teaches(t.id)) &&
        (!problemsOnly || t.problems.length > 0),
    );
  });

  protected readonly filtered = computed(
    () =>
      this.classFilter() !== null ||
      this.teacherFilter() !== null ||
      this.subjectFilter() !== null ||
      this.problemsOnly(),
  );

  protected readonly problemCount = computed(() => {
    const o = this.overview();
    if (!o) {
      return 0;
    }
    return (
      o.classes.reduce(
        (n, c) =>
          n +
          c.problems.length +
          c.rows.filter((r) => r.problems.length).length,
        0,
      ) +
      o.teachers.filter((t) => t.problems.length).length +
      o.groups.filter((g) => g.problems.length).length +
      (o.rvp?.programs.reduce((n, p) => n + p.problems.length, 0) ?? 0)
    );
  });

  protected readonly automaticCount = computed(
    () =>
      (this.overview()?.classes.reduce(
        (n, c) =>
          n + c.rows.filter((r) => r.automatic && r.teacherId !== null).length,
        0,
      ) ?? 0) +
      (this.overview()?.groups.filter(
        (g) => g.automatic && g.teacherId !== null,
      ).length ?? 0),
  );

  constructor() {
    effect(() => {
      const focus = this.focus();
      if (focus) {
        untracked(() => {
          this.clearFilters();
          this.classFilter.set(focus.classId ?? null);
          this.teacherFilter.set(focus.teacherId ?? null);
        });
      }
    });
    this.selection.reload();
    effect(() => {
      const versionId = this.versionId();
      const loaded = this.selection.loaded();
      untracked(() => {
        if (versionId !== null && this.canSeeOverview) {
          this.loadOverview(versionId);
        } else if (loaded) {
          this.loading.set(false);
        }
      });
    });
    // Lessons edited in Rozvrh count in the overview's hours and checks.
    let previous: string | undefined;
    effect(() => {
      const section = this.activeSection();
      untracked(() => {
        if (previous === 'timetable' && this.canSeeOverview) {
          this.reloadOverview();
        }
        previous = section;
      });
    });
    if (this.canSeeOverview) {
      this.teachersApi.getTeachers().subscribe({
        next: (teachers) => this.teachers.set(teachers),
        error: (err) => console.error('getTeachers failed', err),
      });
      this.subjectsApi.getSubjects().subscribe({
        next: (subjects) => this.subjects.set(subjects),
        error: (err) => console.error('getSubjects failed', err),
      });
    }
  }

  private loadOverview(id: number): void {
    this.loading.set(true);
    this.teachingApi.getTeaching(id).subscribe({
      next: (overview) => this.show(overview),
      error: (err) => {
        this.loading.set(false);
        this.notify.actionFailed('getTeaching failed', err);
      },
    });
  }

  /** Groups changed: their plan and the checks again. */
  protected reloadOverview(): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.teachingApi.getTeaching(versionId).subscribe({
        next: (overview) => this.show(overview),
        error: (err) => this.notify.actionFailed('getTeaching failed', err),
      });
    }
  }

  protected show(overview: TeachingOverview): void {
    this.overview.set(overview);
    this.plan = Object.fromEntries(
      overview.curriculum.map((c) => [
        `${c.grade}-${c.subjectId}`,
        c.hoursPerWeek,
      ]),
    );
    this.planBlocks = Object.fromEntries(
      overview.curriculum.map((c) => [
        `${c.grade}-${c.subjectId}`,
        c.doubleLessons ?? null,
      ]),
    );
    this.classPlan = Object.fromEntries(
      overview.classes.flatMap((c) =>
        c.rows.map((r) => [`${c.id}-${r.subjectId}`, r.planHours]),
      ),
    );
    this.loading.set(false);
  }

  private run(request: Observable<TeachingOverview>, done?: string): void {
    this.notify.clear();
    this.busy.set(true);
    request.subscribe({
      next: (overview) => {
        this.busy.set(false);
        this.show(overview);
        if (done) {
          this.notify.success(done);
        }
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('teaching change failed', err);
      },
    });
  }

  /** The automatic choice first, then who has the aprobace for the subject
   * in the class's grade. */
  protected teacherOptions(
    c: TeachingClass,
    row: TeachingRow,
  ): { label: string; value: number | null }[] {
    const qualified = (t: Teacher) =>
      qualifiedFor(t.qualifications, row.subjectId, c.grade);
    const automaticLabel =
      row.automatic && row.teacherName
        ? `Automaticky (${row.teacherName})`
        : 'Automaticky';
    return [
      { label: automaticLabel, value: null },
      ...this.teachers()
        .filter(qualified)
        .map((t) => ({ label: `${t.name} (aprobace)`, value: t.id })),
      ...this.teachers()
        .filter((t) => !qualified(t))
        .map((t) => ({ label: t.name, value: t.id })),
    ];
  }

  protected assign(
    c: TeachingClass,
    row: TeachingRow,
    teacherId: number | null,
  ): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.run(
        this.teachingApi.setAssignment(
          versionId,
          c.id,
          row.subjectId,
          teacherId,
        ),
      );
    }
  }

  /** Saves the class's hours of the subject once the field is left with a
   * different value; the grade's hours again = as the grade. */
  protected saveClassPlan(c: TeachingClass, row: TeachingRow): void {
    const hours = this.classPlan[`${c.id}-${row.subjectId}`] ?? null;
    if (this.busy() || hours === row.planHours) {
      return;
    }
    this.setClassPlan(c, row.subjectId, hours ?? 0);
  }

  protected setClassPlan(
    c: TeachingClass,
    subjectId: number,
    hours: number | null,
  ): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.run(
        this.teachingApi.setClassCurriculum(versionId, c.id, subjectId, hours),
      );
    }
  }

  /** In blocks of two or not; the grade's (or subject's) again = as the grade. */
  protected setClassBlocks(
    c: TeachingClass,
    row: TeachingRow,
    blocks: boolean,
  ): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.run(
        this.teachingApi.setClassDoubleLessons(
          versionId,
          c.id,
          row.subjectId,
          blocks === row.gradeDoubleLessons ? null : blocks,
        ),
      );
    }
  }

  /** The grade's blocks of two: its own, otherwise the subject's. */
  protected gradeBlocks(grade: number, subject: Subject): boolean {
    return this.planBlocks[`${grade}-${subject.id}`] ?? subject.doubleLessons;
  }

  protected toggleGradeBlocks(grade: number, subject: Subject): void {
    const blocks = !this.gradeBlocks(grade, subject);
    this.planBlocks[`${grade}-${subject.id}`] =
      blocks === subject.doubleLessons ? null : blocks;
  }

  /** Subjects the class has nothing of yet. */
  protected addableSubjects(c: TeachingClass): Subject[] {
    return this.subjects().filter(
      (s) => !c.rows.some((r) => r.subjectId === s.id),
    );
  }

  protected clearFilters(): void {
    this.classFilter.set(null);
    this.teacherFilter.set(null);
    this.subjectFilter.set(null);
    this.problemsOnly.set(false);
  }

  protected confirmAutomatic(): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.run(
        this.teachingApi.confirmAssignments(versionId),
        'Automatické přidělení je potvrzené.',
      );
    }
  }

  protected importFromTimetable(): void {
    const versionId = this.versionId();
    if (versionId !== null) {
      this.run(
        this.teachingApi.importFromTimetable(versionId),
        'Plán a přidělení jsou doplněné z rozvrhu.',
      );
    }
  }

  protected savePlan(): void {
    const versionId = this.versionId();
    if (versionId === null) {
      return;
    }
    const items = Object.entries(this.plan)
      .filter(([, hours]) => hours !== null && hours > 0)
      .map(([key, hours]) => {
        const [grade, subjectId] = key.split('-').map(Number);
        return {
          grade,
          subjectId,
          hoursPerWeek: hours!,
          doubleLessons: this.planBlocks[key] ?? null,
        };
      });
    this.run(
      this.teachingApi.saveCurriculum(versionId, items),
      'Učební plán je uložený.',
    );
  }

  /** Plan hours of a grade, for the column footer. */
  protected gradeTotal(grade: number): number {
    return Object.entries(this.plan)
      .filter(([key]) => key.startsWith(`${grade}-`))
      .reduce((sum, [, hours]) => sum + (hours ?? 0), 0);
  }

  protected classProblems(c: TeachingClass): number {
    return c.problems.length + c.rows.filter((r) => r.problems.length).length;
  }
}
