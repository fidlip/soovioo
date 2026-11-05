import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  Grade,
  GradesApiService,
  ReportCardApiService,
  Student,
  StudentReach,
  StudentsApiService,
  Subject,
  reachedSubjectIds,
  reachesStudent,
  SubjectsApiService,
  formatDate,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { GroupLevel, GroupedTable } from '../grouped-table/grouped-table';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS, FloatField } from '../forms';

/** "2026-10-18" (DateOnly) -> local Date, without the UTC shift
 * `new Date(string)` applies to date-only ISO strings. */
function parseDateOnly(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

/** Local (not UTC) y-m-d, see absences-page.ts. */
function toDateOnlyString(date: Date): string {
  const year = date.getFullYear();
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${year}-${month}-${day}`;
}

const DATE_GROUP: GroupLevel<Grade> = {
  label: 'Datum',
  key: (g) => g.gradedOn,
  text: (g) => formatDate(g.gradedOn, 'date'),
};

const STUDENT_GROUP: GroupLevel<Grade> = {
  label: 'Žák',
  key: (g) => g.studentId,
  text: (g) => g.studentName,
};

@Component({
  selector: 'app-grades-page',
  imports: [
    FloatField,
    DateFormatPipe,
    ...FORM_FIELDS,
    TermPipe,
    AppIcon,
    FormsModule,
    Button,
    DatePicker,
    Dialog,
    GroupedTable,
    InputNumber,
    InputText,
    Select,
    Tag,
  ],
  templateUrl: './grades-page.html',
})
export class GradesPage {
  protected readonly notify = inject(NotifyService);
  private readonly gradesApi = inject(GradesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly reportCardApi = inject(ReportCardApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly grades = signal<Grade[] | null>(null);
  protected readonly students = signal<Student[]>([]);
  protected readonly subjects = signal<Subject[]>([]);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  /** Whom GradesWrite reaches — the dialog offers only them (a teacher:
   * what they teach, a group's subject only to its pupils). */
  private readonly reach = signal<StudentReach | null>(null);
  /** The dialog's subject — its pupils depend on it. */
  protected readonly dialogSubjectId = signal<number | null>(null);
  protected readonly dialogSubjects = computed(() => {
    const reached = reachedSubjectIds(this.reach());
    return this.subjects().filter((s) => !reached || reached.has(s.id));
  });
  protected readonly dialogStudentOptions = computed(() => {
    const subjectId = this.dialogSubjectId();
    return this.studentOptions().filter(
      (s) =>
        subjectId !== null && reachesStudent(this.reach(), s.id, subjectId),
    );
  });

  /** null = "all" — separate from the dialog fields below, which drive the
   * add/edit-grade form, not what's shown in the table. */
  protected readonly filterStudentId = signal<number | null>(null);
  protected readonly filterSubjectId = signal<number | null>(null);

  protected readonly isStaff = computed(() => this.authApi.isStaff());

  /** A student only ever sees themself — nothing to filter. */
  protected readonly showStudentFilter = computed(
    () => !this.authApi.isStudent() && this.students().length > 1,
  );

  protected readonly filteredGrades = computed(() => {
    const studentId = this.filterStudentId();
    const subjectId = this.filterSubjectId();
    return (this.grades() ?? []).filter(
      (g) =>
        (studentId === null || g.studentId === studentId) &&
        (subjectId === null || g.subjectId === subjectId),
    );
  });

  /** Staff: date → student. Parent with several children (unfiltered): one
   * group per child. Otherwise a flat list without the student's name. The
   * API already sorts by date desc, then student name — the order the
   * groups need. */
  protected readonly groups = computed<GroupLevel<Grade>[]>(() => {
    if (this.isStaff()) {
      return [DATE_GROUP, this.studentGroup];
    }
    const distinctStudents = new Set(
      this.filteredGrades().map((g) => g.studentId),
    );
    return distinctStudents.size > 1 ? [this.studentGroup] : [];
  });

  private readonly studentGroup: GroupLevel<Grade> = {
    ...STUDENT_GROUP,
    label: localizeStudentTerm(
      STUDENT_GROUP.label,
      this.authApi.getSchoolType(),
    ),
  };

  protected readonly gradeId = (grade: Grade) => grade.id;

  protected readonly gradeDialogVisible = signal(false);
  /** null = add mode, otherwise the grade being edited — same "new… fields
   * double as shared add/edit dialog state" pattern as admin-page.ts. */
  protected editingGradeId: number | null = null;
  protected newStudentId: number | null = null;
  protected newSubjectId: number | null = null;
  protected newValue = 1;
  protected newWeight = 1;
  protected newNote = '';
  protected newGradedOn = new Date();
  /** Drives "Stáhnout vysvědčení" — separate from the dialog's newStudentId,
   * which resets every time the dialog opens. */
  protected reportCardStudentId: number | null = null;

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.reportCardStudentId ??= students[0]?.id ?? null;
      },
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => this.subjects.set(subjects),
      error: (err) => console.error('getSubjects failed', err),
    });
    if (this.canAddGrade()) {
      this.studentsApi.getReach('GradesWrite').subscribe({
        next: (reach) => this.reach.set(reach),
        error: (err) => console.error('getReach failed', err),
      });
    }
  }

  protected onDialogSubjectChange(subjectId: number | null): void {
    this.newSubjectId = subjectId;
    this.dialogSubjectId.set(subjectId);
    if (
      this.newStudentId !== null &&
      !reachesStudent(this.reach(), this.newStudentId, subjectId)
    ) {
      this.newStudentId = null;
    }
  }

  protected openAddGradeDialog(): void {
    this.editingGradeId = null;
    this.newStudentId = this.reportCardStudentId;
    this.onDialogSubjectChange(this.dialogSubjects()[0]?.id ?? null);
    this.newValue = 1;
    this.newWeight = 1;
    this.newNote = '';
    this.newGradedOn = new Date();
    this.notify.clear();
    this.gradeDialogVisible.set(true);
  }

  protected saveGrade(): void {
    if (!this.newStudentId || !this.newSubjectId) {
      return;
    }
    const request = {
      studentId: this.newStudentId,
      subjectId: this.newSubjectId,
      value: this.newValue,
      weight: this.newWeight,
      note: this.newNote || undefined,
      gradedOn: toDateOnlyString(this.newGradedOn),
    };
    const result =
      this.editingGradeId === null
        ? this.gradesApi.addGrade(request)
        : this.gradesApi.updateGrade(this.editingGradeId, request);
    result.subscribe({
      next: () => {
        this.gradeDialogVisible.set(false);
        this.editingGradeId = null;
        this.reload();
      },
      error: (err) => this.notify.actionFailed('saveGrade failed', err),
    });
  }

  protected canAddGrade(): boolean {
    return this.authApi.can('GradesWrite');
  }

  protected startEditGrade(grade: Grade): void {
    this.editingGradeId = grade.id;
    this.newStudentId = grade.studentId;
    this.onDialogSubjectChange(grade.subjectId);
    this.newValue = grade.value;
    this.newWeight = grade.weight;
    this.newNote = grade.note ?? '';
    this.newGradedOn = parseDateOnly(grade.gradedOn);
    this.notify.clear();
    this.gradeDialogVisible.set(true);
  }

  protected deleteGrade(grade: Grade): void {
    this.gradesApi.deleteGrade(grade.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('deleteGrade failed', err),
    });
  }

  /** Czech 1 (best) – 5 (worst) grading scale, mapped to a tag color. */
  protected gradeSeverity(value: number): 'success' | 'warn' | 'danger' {
    if (value <= 2) {
      return 'success';
    }
    return value === 3 ? 'warn' : 'danger';
  }

  protected downloadReportCard(): void {
    if (!this.reportCardStudentId) {
      return;
    }
    this.reportCardApi.downloadReportCard(this.reportCardStudentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'vysvedceni.pdf';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => console.error('downloadReportCard failed', err),
    });
  }

  private reload(): void {
    this.gradesApi.getGrades().subscribe({
      next: (grades) => this.grades.set(grades),
      error: (err) => {
        console.error('getGrades failed', err);
        this.grades.set([]);
      },
    });
  }
}
