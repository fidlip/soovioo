import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  Tab,
  TabList,
  TabPanel,
  TabPanels,
  Tabs,
} from '@openng/optimus-ui/tabs';
import {
  AuthApiService,
  DayOfWeek,
  Subject,
  SubjectsApiService,
  Teacher,
  TeachersApiService,
  TeachingApiService,
  TeachingClass,
  TeachingOverview,
  TeachingRow,
  TimetableVersion,
  TimetableVersionsApiService,
  formatHours,
  qualifiedFor,
  timetableVersionOn,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { AppIcon } from '../app-icon';
import { toDateOnly } from '../departures/retention-labels';
import { FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';
import { SchoolContextService } from '../school-context';
import { versionStatusLabel } from '../timetable-page/timetable-versions-dialog';

const DAY_SHORT: Record<DayOfWeek, string> = {
  Monday: 'Po',
  Tuesday: 'Út',
  Wednesday: 'St',
  Thursday: 'Čt',
  Friday: 'Pá',
  Saturday: 'So',
  Sunday: 'Ne',
};

/** Rozvrh → Přidělení výuky: the učební plán of a timetable version, who
 * teaches what in each class (the system picks where the school didn't)
 * and what doesn't add up. A teacher who may see only their own gets
 * their rows. */
@Component({
  selector: 'app-teaching-page',
  imports: [
    AppIcon,
    Button,
    FloatField,
    FormsModule,
    InputNumber,
    Loading,
    Message,
    RouterLink,
    Select,
    TableModule,
    Tab,
    TabList,
    TabPanel,
    TabPanels,
    Tabs,
    Tag,
  ],
  templateUrl: './teaching-page.html',
})
export class TeachingPage {
  private readonly teachingApi = inject(TeachingApiService);
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly teachersApi = inject(TeachersApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly notify = inject(NotifyService);
  private readonly route = inject(ActivatedRoute);
  protected readonly authApi = inject(AuthApiService);
  protected readonly schoolContext = inject(SchoolContextService);

  protected readonly formatHours = formatHours;

  protected readonly versions = signal<TimetableVersion[]>([]);
  protected readonly versionId = signal<number | null>(null);
  protected readonly overview = signal<TeachingOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  private readonly teachers = signal<Teacher[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected activeTab = 'classes';

  /** Hours being edited in Učební plán, by `grade-subjectId`. */
  protected plan: Record<string, number | null> = {};

  protected readonly versionOptions = computed(() =>
    this.versions().map((v) => ({
      label: `${v.name} (${versionStatusLabel(v, toDateOnly(new Date())).label.toLowerCase()})`,
      value: v.id,
    })),
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
      ) + o.teachers.filter((t) => t.problems.length).length
    );
  });

  protected readonly automaticCount = computed(
    () =>
      this.overview()?.classes.reduce(
        (n, c) =>
          n + c.rows.filter((r) => r.automatic && r.teacherId !== null).length,
        0,
      ) ?? 0,
  );

  /** What each teacher is given, "5.A D 2 h". */
  protected readonly taughtBy = computed(() => {
    const taught = new Map<number, string[]>();
    for (const c of this.overview()?.classes ?? []) {
      for (const r of c.rows) {
        if (r.teacherId !== null) {
          const hours = r.planHours ?? r.timetableHours;
          taught.set(r.teacherId, [
            ...(taught.get(r.teacherId) ?? []),
            `${c.name} ${r.subjectAbbreviation ?? r.subjectName} ${formatHours(hours)}`,
          ]);
        }
      }
    }
    return taught;
  });

  constructor() {
    this.teachersApi.getTeachers().subscribe({
      next: (teachers) => this.teachers.set(teachers),
      error: (err) => console.error('getTeachers failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => this.subjects.set(subjects),
      error: (err) => console.error('getSubjects failed', err),
    });
    this.versionsApi.getVersions().subscribe({
      next: (versions) => {
        this.versions.set(versions);
        const asked = Number(this.route.snapshot.queryParamMap.get('version'));
        const version =
          versions.find((v) => v.id === asked) ??
          timetableVersionOn(versions, toDateOnly(new Date())) ??
          versions[0];
        if (version) {
          this.selectVersion(version.id);
        } else {
          this.loading.set(false);
        }
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.actionFailed('getVersions failed', err);
      },
    });
  }

  protected selectVersion(id: number): void {
    this.versionId.set(id);
    this.loading.set(true);
    this.teachingApi.getTeaching(id).subscribe({
      next: (overview) => this.show(overview),
      error: (err) => {
        this.loading.set(false);
        this.notify.actionFailed('getTeaching failed', err);
      },
    });
  }

  private show(overview: TeachingOverview): void {
    this.overview.set(overview);
    this.plan = Object.fromEntries(
      overview.curriculum.map((c) => [
        `${c.grade}-${c.subjectId}`,
        c.hoursPerWeek,
      ]),
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
        return { grade, subjectId, hoursPerWeek: hours! };
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

  protected days(days: DayOfWeek[]): string {
    return days.length === 5
      ? 'Po–Pá'
      : days.map((d) => DAY_SHORT[d]).join(', ') || '—';
  }

  protected classProblems(c: TeachingClass): number {
    return c.problems.length + c.rows.filter((r) => r.problems.length).length;
  }
}
