import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Grade,
  GradesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
} from '@sova/shared';

@Component({
  selector: 'app-root',
  imports: [FormsModule],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly gradesApi = inject(GradesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);

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

  private reload(): void {
    this.gradesApi.getGrades().subscribe({
      next: (grades) => this.grades.set(grades),
      error: (err) => console.error('getGrades failed', err),
    });
  }
}
