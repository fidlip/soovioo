import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
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
  imports: [FormsModule],
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

  protected selectedStudentId: number | null = null;
  protected selectedSubjectId: number | null = null;
  protected value = 1;

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
      })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addGrade failed', err),
      });
  }

  protected canAddGrade(): boolean {
    return this.authApi.isTeacherOrAdmin();
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
