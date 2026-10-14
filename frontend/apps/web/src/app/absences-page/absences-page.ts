import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  Absence,
  AbsencesApiService,
  Student,
  StudentsApiService,
} from '@sova/shared';

@Component({
  selector: 'app-absences-page',
  imports: [FormsModule],
  templateUrl: './absences-page.html',
})
export class AbsencesPage {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);

  protected readonly absences = signal<Absence[]>([]);
  protected readonly students = signal<Student[]>([]);

  protected selectedStudentId: number | null = null;
  protected date = new Date().toISOString().slice(0, 10);
  protected excuseDrafts: Record<number, string> = {};

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.selectedStudentId ??= students[0]?.id ?? null;
      },
      error: (err) => console.error('getStudents failed', err),
    });
  }

  protected addAbsence(): void {
    if (!this.selectedStudentId) {
      return;
    }
    this.absencesApi
      .addAbsence({ studentId: this.selectedStudentId, date: this.date })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addAbsence failed', err),
      });
  }

  protected submitExcuse(absence: Absence): void {
    const reason = this.excuseDrafts[absence.id];
    if (!reason) {
      return;
    }
    this.absencesApi.submitExcuse(absence.id, reason).subscribe({
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
