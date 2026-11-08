import {
  ChangeDetectorRef,
  Component,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import {
  Tabs,
  Tab,
  TabList,
  TabPanel,
  TabPanels,
} from '@openng/optimus-ui/tabs';
import { Tag } from '@openng/optimus-ui/tag';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  Absence,
  AbsenceStatus,
  AbsencesApiService,
  AuthApiService,
  LINKED_ITEM_GONE,
  DateFormatPipe,
  Excuse,
  Student,
  StudentReach,
  StudentsApiService,
  reachesStudent,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { NotifyService } from '../notify.service';
import { ClearFilters, FloatField, FORM_FIELDS } from '../forms';
import { Loading } from '../loading/loading';
import { highlightElement, onLinkedItem } from '../highlight';
import { formatPeriods, todayString } from './excuse-days';
import { ExcuseDialog } from './excuse-dialog';
import { ExcusesList } from './excuses-list';
import { RejectDialog } from './reject-dialog';
import {
  PeriodChips,
  StudentDayPeriods,
  toDateOnlyString,
} from './student-day-periods';

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

/** The tabs, as `?tab=` has them. */
export type AbsencesTab = 'absence' | 'omluvenky';

/** Docházka: Absence (the days a pupil missed) and Omluvenky (why — the
 * family's, approved by the teacher). Staff start on Absence, families on
 * Omluvenky. */
@Component({
  selector: 'app-absences-page',
  imports: [
    ClearFilters,
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
    ExcuseDialog,
    ExcusesList,
    PeriodChips,
    PrimeTemplate,
    RejectDialog,
    RouterLink,
    Select,
    Tab,
    TableModule,
    TabList,
    TabPanel,
    TabPanels,
    Tabs,
    Tag,
    Tooltip,
  ],
  templateUrl: './absences-page.html',
})
export class AbsencesPage {
  protected readonly notify = inject(NotifyService);
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly changes = inject(ChangeDetectorRef);
  private readonly excuseDialog = viewChild.required(ExcuseDialog);
  private readonly rejectDialog = viewChild.required(RejectDialog);
  private readonly excusesList = viewChild(ExcusesList);

  protected readonly tab = signal<AbsencesTab>(
    this.route.snapshot.queryParamMap.get('tab') === 'absence' ||
      this.route.snapshot.queryParamMap.get('tab') === 'omluvenky'
      ? (this.route.snapshot.queryParamMap.get('tab') as AbsencesTab)
      : this.authApi.isStaff()
        ? 'absence'
        : 'omluvenky',
  );

  protected readonly absences = signal<Absence[] | null>(null);
  protected readonly excuses = signal<Excuse[] | null>(null);
  /** The days' omluvenky, for whether they have files. */
  private readonly excusesById = computed(
    () => new Map((this.excuses() ?? []).map((e) => [e.id, e])),
  );
  private linked: { kind: 'absence' | 'excuse'; id: number } | null = null;
  protected readonly students = signal<Student[]>([]);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  /** Whom AbsencesWrite reaches — the dialog offers only them (a teacher:
   * pupils of their classes and groups). */
  private readonly reach = signal<StudentReach | null>(null);
  protected readonly dialogStudentOptions = computed(() =>
    this.studentOptions().filter((s) => reachesStudent(this.reach(), s.id)),
  );

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
  /** Editing picked from the list (not an excused day, which the add
   * dialog switches to by itself). */
  private editingPicked = false;
  /** The omluvenka's reason when the dialog edits a day under one. */
  protected readonly excuseReason = signal<string | null>(null);
  protected newStudentId: number | null = null;
  protected newDate = new Date();
  /** Missed periods picked in the dialog. */
  protected readonly newPeriods = signal<number[]>([]);
  protected readonly dayPeriods = new StudentDayPeriods();

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
    // ?absence= / ?excuse= — opened from a notification.
    onLinkedItem('absence', (id) => {
      this.linked = { kind: 'absence', id };
      this.setTab('absence');
      this.showLinked();
    });
    onLinkedItem('excuse', (id) => {
      this.linked = { kind: 'excuse', id };
      this.setTab('omluvenky');
      this.showLinked();
    });
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.canManageAbsences()) {
      this.studentsApi.getReach('AbsencesWrite').subscribe({
        next: (reach) => this.reach.set(reach),
        error: (err) => console.error('getReach failed', err),
      });
    }
  }

  protected setTab(tab: AbsencesTab): void {
    this.tab.set(tab);
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams: { tab },
      replaceUrl: true,
    });
  }

  /** Pupil or date changed in the dialog: a day under an omluvenka is
   * edited instead of adding another record; otherwise (unless editing)
   * every period with a lesson is preselected once the timetable is in. */
  protected onDialogStudentOrDateChange(preselect = true): void {
    const date = this.newDate ?? new Date();
    if (!this.editingPicked) {
      const excused = (this.absences() ?? []).find(
        (a) =>
          a.excuseId &&
          a.studentId === this.newStudentId &&
          a.date === toDateOnlyString(date),
      );
      this.editingAbsenceId = excused?.id ?? null;
      this.excuseReason.set(excused?.excuseReason ?? null);
      if (excused) {
        this.newPeriods.set([...excused.periods]);
        preselect = false;
      }
    }
    this.dayPeriods.show(
      this.students().find((s) => s.id === this.newStudentId),
      date,
      () => {
        if (preselect) {
          this.selectLessonPeriods();
        }
      },
    );
  }

  protected selectLessonPeriods(): void {
    this.newPeriods.set(this.dayPeriods.lessonPeriods());
  }

  protected canManageAbsences(): boolean {
    return this.authApi.can('AbsencesWrite');
  }

  /** A parent, or a class teacher for their class. */
  protected canExcuse(): boolean {
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

  protected excuseOf(absence: Absence): Excuse | undefined {
    return absence.excuseId
      ? this.excusesById().get(absence.excuseId)
      : undefined;
  }

  /** A day is approved at the earliest that day. */
  protected approvable(absence: Absence): boolean {
    return absence.date <= todayString();
  }

  protected clearFilters(): void {
    this.filterStudentId = null;
    this.filterStatus = null;
  }

  protected filteredAbsences(): Absence[] {
    return (this.absences() ?? []).filter(
      (a) =>
        (this.filterStudentId === null ||
          a.studentId === this.filterStudentId) &&
        (this.filterStatus === null || a.status === this.filterStatus),
    );
  }

  protected openExcuseDialog(absence?: Absence): void {
    this.excuseDialog().open(
      absence ? { studentId: absence.studentId, date: absence.date } : {},
    );
  }

  protected editExcuse(excuse: Excuse): void {
    this.excuseDialog().open({ excuse });
  }

  protected openAddAbsenceDialog(): void {
    this.editingAbsenceId = null;
    this.editingPicked = false;
    this.excuseReason.set(null);
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
      date: toDateOnlyString(this.newDate),
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

  protected approve(absence: Absence): void {
    this.absencesApi.reviewAbsence(absence.id, true).subscribe({
      next: () => this.reload(),
      error: (err) => this.notify.actionFailed('reviewAbsence failed', err),
    });
  }

  protected reject(absence: Absence): void {
    this.rejectDialog().open(
      `Zamítnout omluvenku — ${absence.studentName}`,
      (note) =>
        this.absencesApi.reviewAbsence(absence.id, false, note).subscribe({
          next: () => this.reload(),
          error: (err) => this.notify.actionFailed('reviewAbsence failed', err),
        }),
    );
  }

  protected startEditAbsence(absence: Absence): void {
    this.editingAbsenceId = absence.id;
    this.editingPicked = true;
    this.excuseReason.set(absence.excuseReason ?? null);
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

  /** What a notification opened, once its list is there. */
  private showLinked(): void {
    const linked = this.linked;
    const list = linked?.kind === 'excuse' ? this.excuses() : this.absences();
    if (!linked || !list) {
      return;
    }
    this.linked = null;
    if (!list.some((item) => item.id === linked.id)) {
      this.notify.warn(LINKED_ITEM_GONE);
      return;
    }
    if (linked.kind === 'excuse') {
      this.excusesList()?.showsAll(linked.id);
    } else if (!this.filteredAbsences().some((a) => a.id === linked.id)) {
      this.clearFilters();
    }
    this.changes.markForCheck();
    highlightElement(`${linked.kind}-${linked.id}`);
  }

  protected reload(): void {
    this.absencesApi.getAbsences().subscribe({
      next: (absences) => {
        this.absences.set(absences);
        this.showLinked();
      },
      error: (err) => {
        console.error('getAbsences failed', err);
        this.absences.set([]);
      },
    });
    this.absencesApi.getExcuses().subscribe({
      next: (excuses) => {
        this.excuses.set(excuses);
        this.showLinked();
      },
      error: (err) => {
        console.error('getExcuses failed', err);
        this.excuses.set([]);
      },
    });
  }
}
