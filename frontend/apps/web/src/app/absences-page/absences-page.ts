import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
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
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';

const STATUS_SEVERITY: Record<
  AbsenceStatus,
  'success' | 'warn' | 'danger' | 'secondary'
> = {
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
  imports: [
    AppIcon,
    FormsModule,
    Button,
    DatePicker,
    Dialog,
    InputText,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
  ],
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
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  protected excuseDrafts: Record<number, string> = {};

  /** null = "all" — separate from the dialog fields below, which drive the
   * add/edit-absence form, not what's shown in the table. */
  protected filterStudentId: number | null = null;
  protected filterStatus: AbsenceStatus | null = null;
  protected readonly statusFilterOptions = Object.entries(STATUS_LABEL).map(
    ([value, label]) => ({
      value: value as AbsenceStatus,
      label,
    }),
  );

  protected readonly absenceDialogVisible = signal(false);
  /** null = add mode, otherwise the absence being edited — same "new…
   * fields double as shared add/edit dialog state" pattern as admin-page.ts. */
  protected editingAbsenceId: number | null = null;
  protected newStudentId: number | null = null;
  protected newDate = new Date();

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
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

  protected statusSeverity(
    status: AbsenceStatus,
  ): 'success' | 'warn' | 'danger' | 'secondary' {
    return STATUS_SEVERITY[status];
  }

  protected statusLabel(status: AbsenceStatus): string {
    return STATUS_LABEL[status];
  }

  protected filteredAbsences(): Absence[] {
    return this.absences().filter(
      (a) =>
        (this.filterStudentId === null ||
          a.studentId === this.filterStudentId) &&
        (this.filterStatus === null || a.status === this.filterStatus),
    );
  }

  protected openAddAbsenceDialog(): void {
    this.editingAbsenceId = null;
    this.newStudentId = this.students()[0]?.id ?? null;
    this.newDate = new Date();
    this.absenceDialogVisible.set(true);
  }

  protected saveAbsence(): void {
    if (!this.newStudentId) {
      return;
    }
    const request = {
      studentId: this.newStudentId,
      date: this.toDateOnlyString(this.newDate),
    };
    const result =
      this.editingAbsenceId === null
        ? this.absencesApi.addAbsence(request)
        : this.absencesApi.updateAbsence(this.editingAbsenceId, request);
    result.subscribe({
      next: () => {
        this.absenceDialogVisible.set(false);
        this.editingAbsenceId = null;
        this.reload();
      },
      error: (err) => console.error('saveAbsence failed', err),
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
    this.newStudentId = absence.studentId;
    this.newDate = new Date(absence.date);
    this.absenceDialogVisible.set(true);
  }

  protected deleteAbsence(absence: Absence): void {
    this.absencesApi.deleteAbsence(absence.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteAbsence failed', err),
    });
  }

  /** Local (not UTC) y-m-d — `toISOString().slice(0, 10)` converts through
   * UTC first, which rolls local midnight back to the previous day in any
   * timezone ahead of UTC (e.g. CET/CEST). */
  private toDateOnlyString(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private reload(): void {
    this.absencesApi.getAbsences().subscribe({
      next: (absences) => this.absences.set(absences),
      error: (err) => console.error('getAbsences failed', err),
    });
  }
}
