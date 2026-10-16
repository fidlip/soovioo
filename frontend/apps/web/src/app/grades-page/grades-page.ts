import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
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
  imports: [FormsModule, Button, Card, InputNumber, InputText, PrimeTemplate, Select, TableModule, Tag],
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
    this.students().map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  protected selectedStudentId: number | null = null;
  protected selectedSubjectId: number | null = null;
  protected value = 1;
  protected weight = 1;

  /** null = "all" — separate from selectedStudentId/selectedSubjectId above,
   * which drive the add-grade form, not what's shown in the table. */
  protected filterStudentId: number | null = null;
  protected filterSubjectId: number | null = null;

  protected editingGradeId: number | null = null;
  protected editStudentId: number | null = null;
  protected editSubjectId: number | null = null;
  protected editValue = 1;
  protected editWeight = 1;
  protected editNote = '';

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.selectedStudentId ??= students[0]?.id ?? null;
      },
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.selectedSubjectId ??= subjects[0]?.id ?? null;
      },
      error: (err) => console.error('getSubjects failed', err),
    });
  }

  protected addGrade(): void {
    if (!this.selectedStudentId || !this.selectedSubjectId) {
      return;
    }
    this.gradesApi
      .addGrade({
        studentId: this.selectedStudentId,
        subjectId: this.selectedSubjectId,
        value: this.value,
        weight: this.weight,
      })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addGrade failed', err),
      });
  }

  protected canAddGrade(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected startEditGrade(grade: Grade): void {
    this.editingGradeId = grade.id;
    this.editStudentId = grade.studentId;
    this.editSubjectId = grade.subjectId;
    this.editValue = grade.value;
    this.editWeight = grade.weight;
    this.editNote = grade.note ?? '';
  }

  protected cancelEditGrade(): void {
    this.editingGradeId = null;
  }

  protected saveEditGrade(): void {
    if (this.editingGradeId === null || !this.editStudentId || !this.editSubjectId) {
      return;
    }
    this.gradesApi
      .updateGrade(this.editingGradeId, {
        studentId: this.editStudentId,
        subjectId: this.editSubjectId,
        value: this.editValue,
        weight: this.editWeight,
        note: this.editNote || undefined,
      })
      .subscribe({
        next: () => {
          this.editingGradeId = null;
          this.reload();
        },
        error: (err) => console.error('updateGrade failed', err),
      });
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
        (this.filterStudentId === null || g.studentId === this.filterStudentId) &&
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
    if (!this.selectedStudentId) {
      return;
    }
    this.reportCardApi.downloadReportCard(this.selectedStudentId).subscribe({
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
