import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { NativeScriptCommonModule } from '@nativescript/angular';
import { Absence, AbsencesApiService, AuthApiService, Student, StudentsApiService } from '@sova/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-absences',
  templateUrl: './absences.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class AbsencesComponent {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly absences = signal<Absence[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected selectedStudentIndex = 0;

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.reload();
  }

  protected canManageAbsences(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  /** Unlike Parent, a Student may never submit their own excuse — the
   * backend rejects it too (AbsencesController.SubmitExcuse), this just
   * keeps the button from appearing in the first place. */
  protected canSubmitExcuse(): boolean {
    return !this.authApi.isStudent();
  }

  protected addAbsence(): void {
    const student = this.students()[this.selectedStudentIndex];
    if (!student) {
      return;
    }
    this.absencesApi
      .addAbsence({ studentId: student.id, date: new Date().toISOString().slice(0, 10) })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addAbsence failed', err),
      });
  }

  protected submitExcuse(absence: Absence): void {
    this.absencesApi.submitExcuse(absence.id, 'Nemoc').subscribe({
      next: () => this.reload(),
      error: (err) => console.error('submitExcuse failed', err),
    });
  }

  protected review(absence: Absence, approve: boolean): void {
    this.absencesApi.reviewAbsence(absence.id, approve).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('reviewAbsence failed', err),
    });
  }

  private reload(): void {
    this.absencesApi.getAbsences().subscribe({
      next: (absences) => this.absences.set(absences),
      error: (err) => console.error('getAbsences failed', err),
    });
  }
}
