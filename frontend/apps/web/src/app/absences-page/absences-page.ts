import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  Absence,
  AbsencesApiService,
  AbsenceStatus,
  AuthApiService,
  Student,
  StudentsApiService,
} from '@sova/shared';

const STATUS_SEVERITY: Record<AbsenceStatus, 'success' | 'warn' | 'danger' | 'secondary'> = {
  Unexcused: 'secondary',
  PendingExcuse: 'warn',
  Excused: 'success',
  Rejected: 'danger',
};

const STATUS_LABEL: Record<AbsenceStatus, string> = {
  Unexcused: 'Neomluveno',
  PendingExcuse: 'Čeká na schválení',
  Excused: 'Omluveno',
  Rejected: 'Zamítnuto',
};

@Component({
  selector: 'app-absences-page',
  imports: [FormsModule, Button, Card, InputText, PrimeTemplate, Select, TableModule, Tag],
  templateUrl: './absences-page.html',
})
export class AbsencesPage {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly absences = signal<Absence[]>([]);
  protected readonly students = signal<Student[]>([]);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  protected selectedStudentId: number | null = null;
  protected date = new Date().toISOString().slice(0, 10);
  protected excuseDrafts: Record<number, string> = {};

  protected editingAbsenceId: number | null = null;
  protected editStudentId: number | null = null;
  protected editDate = '';

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

  protected canManageAbsences(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected statusSeverity(status: AbsenceStatus): 'success' | 'warn' | 'danger' | 'secondary' {
    return STATUS_SEVERITY[status];
  }

  protected statusLabel(status: AbsenceStatus): string {
    return STATUS_LABEL[status];
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

  protected startEditAbsence(absence: Absence): void {
    this.editingAbsenceId = absence.id;
    this.editStudentId = absence.studentId;
    this.editDate = absence.date;
  }

  protected cancelEditAbsence(): void {
    this.editingAbsenceId = null;
  }

  protected saveEditAbsence(): void {
    if (this.editingAbsenceId === null || !this.editStudentId) {
      return;
    }
    this.absencesApi
      .updateAbsence(this.editingAbsenceId, { studentId: this.editStudentId, date: this.editDate })
      .subscribe({
        next: () => {
          this.editingAbsenceId = null;
          this.reload();
        },
        error: (err) => console.error('updateAbsence failed', err),
      });
  }

  protected deleteAbsence(absence: Absence): void {
    this.absencesApi.deleteAbsence(absence.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteAbsence failed', err),
    });
  }

  private reload(): void {
    this.absencesApi.getAbsences().subscribe({
      next: (absences) => this.absences.set(absences),
      error: (err) => console.error('getAbsences failed', err),
    });
  }
}
