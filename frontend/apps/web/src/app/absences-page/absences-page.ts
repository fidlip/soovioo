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
  AbsenceStatus,
  AbsencesApiService,
  AuthApiService,
  DateFormatPipe,
  DayOfWeek,
  SchoolPeriod,
  SchoolPeriodsApiService,
  Student,
  StudentsApiService,
  TimetableApiService,
  TimetableEntry,
  weekParityOf,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS, FloatField } from '../forms';
import { Loading } from '../loading/loading';

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

const DAYS_BY_INDEX: DayOfWeek[] = [
  'Sunday',
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
];

/** [1,2,3,5] -> "1.–3., 5."; [] -> "celý den". */
export function formatPeriods(periods: number[]): string {
  if (periods.length === 0) {
    return 'celý den';
  }
  const ranges: string[] = [];
  let start = periods[0];
  let prev = periods[0];
  for (const p of [...periods.slice(1), Number.NaN]) {
    if (p === prev + 1) {
      prev = p;
      continue;
    }
    ranges.push(start === prev ? `${start}.` : `${start}.–${prev}.`);
    start = p;
    prev = p;
  }
  return ranges.join(', ');
}

@Component({
  selector: 'app-absences-page',
  imports: [
    FloatField,
    DateFormatPipe,
    ...FORM_FIELDS,
    Loading,
    TermPipe,
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
  protected readonly notify = inject(NotifyService);
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly timetableApi = inject(TimetableApiService);
  private readonly schoolPeriodsApi = inject(SchoolPeriodsApiService);

  protected readonly absences = signal<Absence[] | null>(null);
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
  /** Missed periods picked in the dialog (see `dayPeriods`). */
  protected readonly newPeriods = signal<number[]>([]);
  protected readonly schoolPeriods = signal<SchoolPeriod[]>([]);
  /** Timetable of the dialog's student's class, cached per class id. */
  private readonly classTimetables = signal<Record<number, TimetableEntry[]>>(
    {},
  );
  private readonly dialogStudentId = signal<number | null>(null);
  private readonly dialogDate = signal(new Date());

  /** The school's periods for the dialog's date, each with the lesson the
   * student's class has then (by timetable, incl. odd/even weeks) — the
   * chips to pick missed periods from. */
  protected readonly dayPeriods = computed(() => {
    const student = this.students().find(
      (s) => s.id === this.dialogStudentId(),
    );
    const date = this.dialogDate();
    const entries = student
      ? (this.classTimetables()[student.schoolClassId] ?? [])
      : [];
    const day = DAYS_BY_INDEX[date.getDay()];
    const parity = weekParityOf(date);
    return this.schoolPeriods().map((period) => {
      const entry = entries.find(
        (e) =>
          e.dayOfWeek === day &&
          e.periodNumber === period.periodNumber &&
          (e.weekParity === 'Every' || e.weekParity === parity),
      );
      return {
        number: period.periodNumber,
        lesson: entry ? (entry.subjectAbbreviation ?? entry.subjectName) : null,
      };
    });
  });

  protected readonly formatPeriods = formatPeriods;

  /** Missed lessons over the filtered records; whole-day records (no
   * period detail) are counted separately. */
  protected missedSummary(): {
    total: number;
    excused: number;
    unexcused: number;
    wholeDays: number;
  } {
    let excused = 0;
    let unexcused = 0;
    let wholeDays = 0;
    for (const a of this.filteredAbsences()) {
      if (a.periods.length === 0) {
        wholeDays++;
      } else if (a.status === 'Excused') {
        excused += a.periods.length;
      } else {
        unexcused += a.periods.length;
      }
    }
    return { total: excused + unexcused, excused, unexcused, wholeDays };
  }

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.canManageAbsences()) {
      this.schoolPeriodsApi.getPeriods().subscribe({
        next: (periods) => this.schoolPeriods.set(periods),
        error: (err) => console.error('getPeriods failed', err),
      });
    }
  }

  protected isPeriodSelected(number: number): boolean {
    return this.newPeriods().includes(number);
  }

  protected togglePeriod(number: number): void {
    this.newPeriods.update((periods) =>
      periods.includes(number)
        ? periods.filter((p) => p !== number)
        : [...periods, number].sort((a, b) => a - b),
    );
  }

  /** Student or date changed in the dialog: load that class's timetable if
   * needed, then (unless editing) preselect every period with a lesson. */
  protected onDialogStudentOrDateChange(preselect = true): void {
    this.dialogStudentId.set(this.newStudentId);
    this.dialogDate.set(this.newDate ?? new Date());
    const student = this.students().find((s) => s.id === this.newStudentId);
    if (!student) {
      return;
    }
    const select = () => {
      if (preselect) {
        this.newPeriods.set(
          this.dayPeriods()
            .filter((p) => p.lesson !== null)
            .map((p) => p.number),
        );
      }
    };
    if (this.classTimetables()[student.schoolClassId]) {
      select();
      return;
    }
    this.timetableApi.getTimetable(student.schoolClassId).subscribe({
      next: (entries) => {
        this.classTimetables.update((m) => ({
          ...m,
          [student.schoolClassId]: entries,
        }));
        select();
      },
      error: (err) => console.error('getTimetable failed', err),
    });
  }

  protected selectLessonPeriods(): void {
    this.newPeriods.set(
      this.dayPeriods()
        .filter((p) => p.lesson !== null)
        .map((p) => p.number),
    );
  }

  protected canManageAbsences(): boolean {
    return this.authApi.can('AbsencesWrite');
  }

  protected canSubmitExcuse(): boolean {
    return this.authApi.can('AbsencesExcuse');
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
    return (this.absences() ?? []).filter(
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
    this.newPeriods.set([]);
    this.onDialogStudentOrDateChange();
    this.notify.clear();
    this.absenceDialogVisible.set(true);
  }

  protected saveAbsence(): void {
    if (!this.newStudentId) {
      return;
    }
    const request = {
      studentId: this.newStudentId,
      date: this.toDateOnlyString(this.newDate),
      periods: this.newPeriods(),
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
      error: (err) => this.notify.actionFailed('saveAbsence failed', err),
    });
  }

  protected submitExcuse(absence: Absence): void {
    const reason = this.excuseDrafts[absence.id];
    if (!reason) {
      return;
    }
    this.absencesApi.submitExcuse(absence.id, reason).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('submitExcuse failed', err),
    });
  }

  protected review(absence: Absence, approve: boolean): void {
    this.absencesApi.reviewAbsence(absence.id, approve).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('reviewAbsence failed', err),
    });
  }

  protected startEditAbsence(absence: Absence): void {
    this.editingAbsenceId = absence.id;
    this.newStudentId = absence.studentId;
    this.newDate = new Date(`${absence.date}T00:00:00`);
    this.newPeriods.set([...absence.periods]);
    this.onDialogStudentOrDateChange(false);
    this.notify.clear();
    this.absenceDialogVisible.set(true);
  }

  protected deleteAbsence(absence: Absence): void {
    this.absencesApi.deleteAbsence(absence.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('deleteAbsence failed', err),
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
      error: (err) => {
        console.error('getAbsences failed', err);
        this.absences.set([]);
      },
    });
  }
}
