import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { NativeScriptCommonModule } from '@nativescript/angular';
import {
  AuthApiService,
  Grade,
  GradesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
} from '@soovioo/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-home',
  templateUrl: './home.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class HomeComponent {
  private readonly gradesApi = inject(GradesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly grades = signal<Grade[]>([]);
  private firstStudent: Student | undefined;
  private firstSubject: Subject | undefined;

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => (this.firstStudent = students[0]),
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => (this.firstSubject = subjects[0]),
      error: (err) => console.error('getSubjects failed', err),
    });
  }

  protected canAddGrade(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected addDemoGrade(): void {
    if (!this.firstStudent || !this.firstSubject) {
      return;
    }
    this.gradesApi
      .addGrade({
        studentId: this.firstStudent.id,
        subjectId: this.firstSubject.id,
        value: 1,
        weight: 2,
        note: 'Pololetní práce',
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
