import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  Grade,
  GradesApiService,
  ReportCardApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
} from '@sova/shared';

@Component({
  selector: 'app-grades-page',
  imports: [
    FormsModule,
    Button,
    Card,
    Dialog,
    InputNumber,
    InputText,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
  ],
  templateUrl: './grades-page.html',
})
export class GradesPage {
  private readonly gradesApi = inject(GradesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly reportCardApi = inject(ReportCardApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly grades = signal<Grade[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly subjects = signal<Subject[]>([]);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  /** null = "all" — separate from the dialog fields below, which drive the
   * add/edit-grade form, not what's shown in the table. */
  protected filterStudentId: number | null = null;
  protected filterSubjectId: number | null = null;

  protected readonly gradeDialogVisible = signal(false);
  /** null = add mode, otherwise the grade being edited — same "new… fields
   * double as shared add/edit dialog state" pattern as admin-page.ts. */
  protected editingGradeId: number | null = null;
  protected newStudentId: number | null = null;
  protected newSubjectId: number | null = null;
  protected newValue = 1;
  protected newWeight = 1;
  protected newNote = '';
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
  }

  protected openAddGradeDialog(): void {
    this.editingGradeId = null;
    this.newStudentId = this.reportCardStudentId;
    this.newSubjectId = this.subjects()[0]?.id ?? null;
    this.newValue = 1;
    this.newWeight = 1;
    this.newNote = '';
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
      error: (err) => console.error('saveGrade failed', err),
    });
  }

  protected canAddGrade(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected startEditGrade(grade: Grade): void {
    this.editingGradeId = grade.id;
    this.newStudentId = grade.studentId;
    this.newSubjectId = grade.subjectId;
    this.newValue = grade.value;
    this.newWeight = grade.weight;
    this.newNote = grade.note ?? '';
    this.gradeDialogVisible.set(true);
  }

  protected deleteGrade(grade: Grade): void {
    this.gradesApi.deleteGrade(grade.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteGrade failed', err),
    });
  }

  protected filteredGrades(): Grade[] {
    return this.grades().filter(
      (g) =>
        (this.filterStudentId === null ||
          g.studentId === this.filterStudentId) &&
        (this.filterSubjectId === null || g.subjectId === this.filterSubjectId),
    );
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
      error: (err) => console.error('getGrades failed', err),
    });
  }
}
