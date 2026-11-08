import {
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  Classroom,
  ClassroomsApiService,
  GroupDivision,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentGroup,
  StudentGroups,
  StudentsApiService,
  Subject,
  Teacher,
  TeachingApiService,
  TeachingGroup,
  formatHours,
  qualifiedFirst,
  qualifiedFor,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { ClassPicker, FloatField } from '../forms';
import { NotifyService } from '../notify.service';

interface DivisionForm {
  id: number | null;
  name: string;
  /** null = as the učební plán. */
  hoursPerWeek: number | null;
  doubleLessons: boolean;
  classIds: number[];
  coversSubjectId: number | null;
}

interface GroupForm {
  id: number | null;
  divisionId: number | null;
  name: string;
  subjectId: number | null;
  /** null = as the učební plán. */
  hoursPerWeek: number | null;
  doubleLessons: boolean;
  teacherId: number | null;
  classroomId: number | null;
  classIds: number[];
  studentIds: number[];
}

/** Administrace → Výuka → Skupiny: dělení tříd and groups on
 * their own, with who's in them. The plan and přidělení of each group
 * come from `rows` (the overview's groups). */
@Component({
  selector: 'app-student-groups',
  imports: [
    Button,
    Checkbox,
    Dialog,
    FloatField,
    FormsModule,
    InputNumber,
    InputText,
    ClassPicker,
    MultiSelect,
    NgTemplateOutlet,
    Select,
    TableModule,
    Tag,
  ],
  templateUrl: './student-groups.html',
})
export class StudentGroupsPanel {
  private readonly teachingApi = inject(TeachingApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly classesApi = inject(SchoolClassesApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  protected readonly formatHours = formatHours;

  readonly versionId = input.required<number>();
  readonly canEdit = input(false);
  /** A published version: of a group only who's in it. */
  readonly membersOnly = input(false);
  /** The plan and přidělení of each group. */
  readonly rows = input<TeachingGroup[]>([]);
  readonly subjects = input<Subject[]>([]);
  readonly teachers = input<Teacher[]>([]);
  /** Narrows the lists like the tab's filters. */
  readonly classFilter = input<number | null>(null);
  readonly teacherFilter = input<number | null>(null);
  readonly subjectFilter = input<number | null>(null);
  readonly problemsOnly = input(false);
  /** Something changed: the plan's checks need loading again. */
  readonly changed = output<void>();

  protected readonly canEditWhole = computed(
    () => this.canEdit() && !this.membersOnly(),
  );

  protected readonly data = signal<StudentGroups>({
    divisions: [],
    groups: [],
  });
  private readonly students = signal<Student[]>([]);
  protected readonly classes = signal<SchoolClass[]>([]);
  private readonly classrooms = signal<Classroom[]>([]);
  protected readonly busy = signal(false);

  protected readonly classroomOptions = computed(() => [
    { label: 'Podle předmětu', value: null as number | null },
    ...this.classrooms().map((r) => ({
      label: r.capacity ? `${r.name} (${r.capacity})` : r.name,
      value: r.id as number | null,
    })),
  ]);

  private readonly rowsById = computed(
    () => new Map(this.rows().map((r) => [r.id, r])),
  );

  /** Groups the filters let through. */
  private readonly shown = computed(() => {
    const classId = this.classFilter();
    const teacherId = this.teacherFilter();
    const subjectId = this.subjectFilter();
    const problemsOnly = this.problemsOnly();
    return this.data().groups.filter((g) => {
      const row = this.rowsById().get(g.id);
      return (
        (classId === null || g.classIds.includes(classId)) &&
        (teacherId === null || row?.teacherId === teacherId) &&
        (subjectId === null || g.subjectId === subjectId) &&
        (!problemsOnly || (row?.problems.length ?? 0) > 0)
      );
    });
  });

  protected readonly filtered = computed(
    () =>
      this.classFilter() !== null ||
      this.teacherFilter() !== null ||
      this.subjectFilter() !== null ||
      this.problemsOnly(),
  );

  protected readonly divisions = computed(() =>
    this.data()
      .divisions.map((d) => ({
        division: d,
        groups: this.shown().filter((g) => g.divisionId === d.id),
      }))
      .filter(
        (d) =>
          d.groups.length > 0 || !this.filtered() || this.onlyClass(d.division),
      ),
  );
  /** Only the class filter is on and the division is of the class. */
  private onlyClass(division: GroupDivision): boolean {
    const classId = this.classFilter();
    return (
      classId !== null &&
      this.teacherFilter() === null &&
      this.subjectFilter() === null &&
      !this.problemsOnly() &&
      division.classIds.includes(classId)
    );
  }

  protected readonly freeGroups = computed(() =>
    this.shown().filter((g) => g.divisionId === null),
  );

  protected readonly divisionForm = signal<DivisionForm | null>(null);
  protected readonly groupForm = signal<GroupForm | null>(null);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly injector = inject(Injector);
  /** Shown once the list has it: the division or group just made. */
  private scrollTo: string | null = null;

  constructor() {
    effect(() => {
      const versionId = this.versionId();
      untracked(() => this.load(versionId));
    });
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.classesApi.getSchoolClasses().subscribe({
      next: (classes) => this.classes.set(classes),
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    this.classroomsApi.getClassrooms().subscribe({
      next: (classrooms) => this.classrooms.set(classrooms),
      error: (err) => console.error('getClassrooms failed', err),
    });
  }

  private load(versionId: number): void {
    this.teachingApi.getGroups(versionId).subscribe({
      next: (data) => {
        this.data.set(data);
        const id = this.scrollTo;
        this.scrollTo = null;
        if (id) {
          afterNextRender(
            () =>
              (this.host.nativeElement as HTMLElement)
                .querySelector(`#${id}`)
                ?.scrollIntoView({ block: 'center', behavior: 'smooth' }),
            { injector: this.injector },
          );
        }
      },
      error: (err) => this.notify.actionFailed('getGroups failed', err),
    });
  }

  protected row(group: StudentGroup): TeachingGroup | undefined {
    return this.rowsById().get(group.id);
  }

  /** "3 h", or "podle plánu (2 h)". */
  protected hoursLabel(hours: number | null, groupIds: number[]): string {
    if (hours !== null) {
      return formatHours(hours);
    }
    const planned = this.planHours(groupIds);
    return planned ? `podle plánu (${planned})` : 'podle plánu';
  }

  /** "2 h" by the učební plán, from a group's row; '' when unknown. */
  protected planHours(groupIds: number[]): string {
    const row = groupIds
      .map((id) => this.rowsById().get(id))
      .find((r) => r !== undefined);
    return row ? formatHours(row.planHours) : '';
  }

  protected divisionGroupIds(divisionId: number | null): number[] {
    return this.data()
      .groups.filter((g) => g.divisionId === divisionId)
      .map((g) => g.id);
  }

  protected classNames(classIds: number[]): string {
    return this.classes()
      .filter((c) => classIds.includes(c.id))
      .map((c) => c.name)
      .join(', ');
  }

  protected subjectName(subjectId: number): string {
    const subject = this.subjects().find((s) => s.id === subjectId);
    return subject?.name ?? '';
  }

  protected classroomName(classroomId: number | null): string {
    return classroomId === null
      ? 'podle předmětu'
      : (this.classrooms().find((r) => r.id === classroomId)?.name ?? '');
  }

  /** The automatic choice first, then who has the aprobace. */
  protected teacherOptions(form: GroupForm): {
    label: string;
    value: number | null;
  }[] {
    const grades = new Set(
      this.classes()
        .filter((c) => form.classIds.includes(c.id))
        .map((c) => c.rocnik),
    );
    const grade = grades.size === 1 ? [...grades][0] : null;
    const row = form.id === null ? undefined : this.rowsById().get(form.id);
    return [
      {
        label:
          row?.automatic && row.teacherName
            ? `Automaticky (${row.teacherName})`
            : 'Automaticky',
        value: null,
      },
      ...qualifiedFirst(
        this.teachers().map((t) => ({
          id: t.id,
          label: t.name,
          qualifications: t.qualifications,
        })),
        (t) =>
          form.subjectId !== null &&
          qualifiedFor(t.qualifications, form.subjectId, grade ?? null),
      ).map((t) => ({ label: t.label, value: t.id as number | null })),
    ];
  }

  /** Pupils of the form's classes; in a division, who's in another of its
   * groups can't be chosen and says where they are. */
  protected studentOptions(
    form: GroupForm,
  ): { label: string; value: number; disabled: boolean }[] {
    const classIds = this.formClassIds(form);
    const others = new Map<number, string>();
    if (form.divisionId !== null) {
      for (const g of this.data().groups) {
        if (g.divisionId === form.divisionId && g.id !== form.id) {
          g.studentIds.forEach((id) => others.set(id, g.name));
        }
      }
    }
    return this.students()
      .filter((s) => classIds.includes(s.schoolClassId))
      .sort(
        (a, b) =>
          a.schoolClassName.localeCompare(b.schoolClassName, 'cs') ||
          a.lastName.localeCompare(b.lastName, 'cs') ||
          a.firstName.localeCompare(b.firstName, 'cs'),
      )
      .map((s) => ({
        label: `${s.lastName} ${s.firstName} (${s.schoolClassName})${others.has(s.id) ? ` — ve skupině ${others.get(s.id)}` : ''}`,
        value: s.id,
        disabled: others.has(s.id),
      }));
  }

  protected formClassIds(form: GroupForm): number[] {
    return form.divisionId === null
      ? form.classIds
      : (this.data().divisions.find((d) => d.id === form.divisionId)
          ?.classIds ?? []);
  }

  protected newDivision(): void {
    this.notify.clear();
    this.divisionForm.set({
      id: null,
      name: '',
      hoursPerWeek: null,
      doubleLessons: false,
      classIds: [],
      coversSubjectId: null,
    });
  }

  protected editDivision(d: GroupDivision): void {
    this.notify.clear();
    this.divisionForm.set({
      id: d.id,
      name: d.name,
      hoursPerWeek: d.hoursPerWeek,
      doubleLessons: d.doubleLessons ?? false,
      classIds: [...d.classIds],
      coversSubjectId: d.coversSubjectId,
    });
  }

  protected saveDivision(form: DivisionForm): void {
    const request = {
      name: form.name,
      hoursPerWeek: form.hoursPerWeek,
      doubleLessons: form.doubleLessons || null,
      classIds: form.classIds,
      coversSubjectId: form.coversSubjectId,
    };
    const versionId = this.versionId();
    this.run(
      form.id === null
        ? this.teachingApi.createDivision(versionId, request)
        : this.teachingApi.updateDivision(versionId, form.id, request),
      (d) => {
        this.divisionForm.set(null);
        if (form.id === null) {
          this.scrollTo = `division-${d.id}`;
        }
      },
    );
  }

  protected deleteDivision(d: GroupDivision): void {
    this.confirmation.confirm({
      header: 'Smazat dělení',
      message: `Smazat dělení ${d.name} i s jeho skupinami a jejich hodinami v rozvrhu?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(this.teachingApi.deleteDivision(this.versionId(), d.id)),
    });
  }

  protected newGroup(division: GroupDivision | null): void {
    this.notify.clear();
    this.groupForm.set({
      id: null,
      divisionId: division?.id ?? null,
      name: '',
      subjectId: this.subjectFilter() ?? this.subjects()[0]?.id ?? null,
      hoursPerWeek: division?.hoursPerWeek ?? null,
      doubleLessons: division?.doubleLessons ?? false,
      teacherId: null,
      classroomId: null,
      classIds: division ? [...division.classIds] : [],
      studentIds: [],
    });
  }

  protected editGroup(g: StudentGroup): void {
    this.notify.clear();
    this.groupForm.set({
      id: g.id,
      divisionId: g.divisionId,
      name: g.name,
      subjectId: g.subjectId,
      hoursPerWeek: g.hoursPerWeek,
      doubleLessons:
        g.doubleLessons ??
        this.subjects().find((s) => s.id === g.subjectId)?.doubleLessons ??
        false,
      teacherId: g.teacherId,
      classroomId: g.classroomId,
      classIds: [...g.classIds],
      studentIds: [...g.studentIds],
    });
  }

  /** Pupils not in the form's classes any more drop out. */
  protected onFormClassesChange(form: GroupForm): void {
    const classIds = this.formClassIds(form);
    form.studentIds = form.studentIds.filter((id) =>
      this.students().some(
        (s) => s.id === id && classIds.includes(s.schoolClassId),
      ),
    );
  }

  protected saveGroup(form: GroupForm): void {
    if (form.subjectId === null) {
      return;
    }
    if (this.membersOnly() && form.id !== null) {
      this.run(
        this.teachingApi.updateGroupMembers(
          this.versionId(),
          form.id,
          form.studentIds,
        ),
        () => this.groupForm.set(null),
      );
      return;
    }
    const subject = this.subjects().find((s) => s.id === form.subjectId);
    const request = {
      name: form.name,
      subjectId: form.subjectId,
      divisionId: form.divisionId,
      hoursPerWeek: form.hoursPerWeek,
      doubleLessons:
        form.doubleLessons === (subject?.doubleLessons ?? false)
          ? null
          : form.doubleLessons,
      teacherId: form.teacherId,
      classroomId: form.classroomId,
      classIds: form.classIds,
      studentIds: form.studentIds,
    };
    const versionId = this.versionId();
    this.run(
      form.id === null
        ? this.teachingApi.createGroup(versionId, request)
        : this.teachingApi.updateGroup(versionId, form.id, request),
      (g) => {
        this.groupForm.set(null);
        if (form.id === null) {
          this.scrollTo = `group-${g.id}`;
        }
      },
    );
  }

  protected deleteGroup(g: StudentGroup): void {
    this.confirmation.confirm({
      header: 'Smazat skupinu',
      message: `Smazat skupinu ${g.name} i s jejími hodinami v rozvrhu?`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(this.teachingApi.deleteGroup(this.versionId(), g.id)),
    });
  }

  private run<T>(request: Observable<T>, done?: (result: T) => void): void {
    this.notify.clear();
    this.busy.set(true);
    request.subscribe({
      next: (result) => {
        this.busy.set(false);
        done?.(result);
        this.load(this.versionId());
        this.changed.emit();
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('group change failed', err);
      },
    });
  }
}
