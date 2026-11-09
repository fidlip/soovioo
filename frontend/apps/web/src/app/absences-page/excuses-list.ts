import {
  Component,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  AbsenceStatus,
  AbsencesApiService,
  ExcuseDay,
  formatDate,
  AuthApiService,
  EXCUSE_CHANNEL_LABELS,
  Excuse,
  ExcuseStatus,
  nameText,
} from '@soovioo/shared';
import { ClearFilters, FloatField } from '../forms';
import { GroupedTable, GroupLevel } from '../grouped-table/grouped-table';
import { NotifyService } from '../notify.service';
import { DecisionMenu } from './decision-menu';
import { excuseFileDownloader, formatFileSize } from './excuse-files';
import { RejectDialog } from './reject-dialog';
import { excuseRange, formatPeriods, todayString } from './excuse-days';
import { PersonName } from '../person-name/person-name';

export const EXCUSE_STATUS_LABEL: Record<ExcuseStatus, string> = {
  Pending: 'Čeká na schválení',
  Approved: 'Schválena',
  Rejected: 'Zamítnuta',
  PartlyApproved: 'Částečně schválena',
};

export const EXCUSE_STATUS_SEVERITY: Record<
  ExcuseStatus,
  'success' | 'warn' | 'danger' | 'info'
> = {
  Pending: 'warn',
  Approved: 'success',
  Rejected: 'danger',
  PartlyApproved: 'info',
};

/** The Omluvenky tab: who, which days, why, files, the teacher's decision.
 * The teacher approves or rejects a whole omluvenka here, changes the
 * decision, edits or deletes it; the family edits or deletes one while days
 * of it wait. */
@Component({
  selector: 'app-excuses-list',
  imports: [
    PersonName,
    ClearFilters,
    DecisionMenu,
    FloatField,
    GroupedTable,
    FormsModule,
    NgTemplateOutlet,
    Button,
    RejectDialog,
    Select,
    Tag,
    Tooltip,
  ],
  templateUrl: './excuses-list.html',
})
export class ExcusesList {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);
  private readonly rejectDialog = viewChild.required(RejectDialog);
  protected readonly download = excuseFileDownloader();

  readonly excuses = input.required<Excuse[] | null>();
  /** Editing asked for (the page owns the dialog). */
  readonly edit = output<Excuse>();
  /** Something changed — reload. */
  readonly changed = output<void>();

  protected filterStudentId: number | null = null;
  protected filterStatus: ExcuseStatus | null = null;
  protected readonly statusLabel = EXCUSE_STATUS_LABEL;
  protected readonly statusSeverity = EXCUSE_STATUS_SEVERITY;
  protected readonly statusOptions = Object.entries(EXCUSE_STATUS_LABEL).map(
    ([value, label]) => ({ value: value as ExcuseStatus, label }),
  );
  protected readonly studentOptions = computed(() => {
    const seen = new Map<number, string>();
    for (const e of this.excuses() ?? []) {
      seen.set(e.studentId, nameText(e.studentName, e.studentLabel));
    }
    return [...seen].map(([id, label]) => ({ id, label }));
  });
  protected readonly fileSize = formatFileSize;
  protected readonly periods = formatPeriods;
  protected readonly range = excuseRange;
  protected readonly isStaff = computed(() => this.authApi.isStaff());
  private readonly byStudent: GroupLevel<Excuse>[] = [
    {
      label: 'Žák',
      key: (e) => e.studentId,
      text: (e) => e.studentName,
      nameLabel: (e) => e.studentLabel,
    },
  ];
  protected readonly excuseId = (excuse: Excuse) => excuse.id;
  protected readonly excuseElementId = (excuse: Excuse) =>
    `excuse-${excuse.id}`;

  /** As on Absence: a family with several children (unfiltered) sees one
   * group per child, otherwise a flat list. */
  protected groups(): GroupLevel<Excuse>[] {
    return !this.isStaff() &&
      this.filterStudentId === null &&
      this.studentOptions().length > 1
      ? this.byStudent
      : [];
  }

  /** One child's omluvenky together, each child's still newest first. */
  protected rows(): Excuse[] {
    const rows = this.filtered();
    return this.groups().length > 0
      ? [...rows].sort((a, b) => a.studentName.localeCompare(b.studentName))
      : rows;
  }

  protected filtered(): Excuse[] {
    return (this.excuses() ?? []).filter(
      (e) =>
        (this.filterStudentId === null ||
          e.studentId === this.filterStudentId) &&
        (this.filterStatus === null || e.status === this.filterStatus),
    );
  }

  /** Shows the omluvenka even when the filters hid it. */
  showsAll(id: number): void {
    if (!this.filtered().some((e) => e.id === id)) {
      this.clearFilters();
    }
  }

  protected clearFilters(): void {
    this.filterStudentId = null;
    this.filterStatus = null;
  }

  protected canReview(): boolean {
    return this.authApi.can('AbsencesWrite');
  }

  protected isFamily(): boolean {
    return this.authApi.scope('AbsencesExcuse') === 'Own';
  }

  /** "telefonicky" — how the family gave it to whoever wrote it down. */
  protected channelLabel(excuse: Excuse): string {
    return excuse.channel
      ? EXCUSE_CHANNEL_LABELS[excuse.channel].toLowerCase()
      : '';
  }

  protected status(excuse: Excuse): ExcuseStatus {
    return excuse.status;
  }

  protected waiting(excuse: Excuse): boolean {
    return excuse.days.some((d) => d.status === 'PendingExcuse');
  }

  /** A day to approve has already come — a waiting one while some wait. */
  protected approvable(excuse: Excuse): boolean {
    const today = todayString();
    const waiting = this.waiting(excuse);
    return excuse.days.some(
      (d) =>
        (waiting ? d.status === 'PendingExcuse' : d.status !== 'Excused') &&
        d.date <= today,
    );
  }

  protected has(excuse: Excuse, status: AbsenceStatus): boolean {
    return excuse.days.some((d) => d.status === status);
  }

  private readonly dayLabel: Record<AbsenceStatus, string> = {
    Unexcused: 'neomluveno',
    PendingExcuse: 'čeká na schválení',
    Excused: 'omluveno',
    Rejected: 'zamítnuto',
  };

  /** "6. 11. 2026: zamítnuto — Bez lékařské zprávy". */
  protected dayText(day: ExcuseDay): string {
    return `${formatDate(day.date, 'date')}: ${this.dayLabel[day.status]}${
      day.reviewNote ? ` — ${day.reviewNote}` : ''
    }`;
  }

  /** The teacher decided its days differently (or only some so far) —
   * each day is listed with its state. */
  protected mixed(excuse: Excuse): boolean {
    return new Set(excuse.days.map((d) => d.status)).size > 1;
  }

  protected notes(excuse: Excuse): string[] {
    return [
      ...new Set(
        excuse.days
          .filter((d) => d.status === 'Rejected' && d.reviewNote)
          .map((d) => d.reviewNote as string),
      ),
    ];
  }

  protected approve(excuse: Excuse): void {
    this.absencesApi.reviewExcuse(excuse.id, true).subscribe({
      next: () => this.changed.emit(),
      error: (err) => this.notify.actionFailed('reviewExcuse failed', err),
    });
  }

  protected reject(excuse: Excuse): void {
    this.rejectDialog().open(
      `Zamítnout omluvenku — ${nameText(excuse.studentName, excuse.studentLabel)}`,
      (note) =>
        this.absencesApi.reviewExcuse(excuse.id, false, note).subscribe({
          next: () => this.changed.emit(),
          error: (err) => this.notify.actionFailed('reviewExcuse failed', err),
        }),
    );
  }

  protected reopen(excuse: Excuse): void {
    this.absencesApi.reopenExcuse(excuse.id).subscribe({
      next: () => this.changed.emit(),
      error: (err) => this.notify.actionFailed('reopenExcuse failed', err),
    });
  }

  protected remove(excuse: Excuse): void {
    this.absencesApi.deleteExcuse(excuse.id).subscribe({
      next: () => this.changed.emit(),
      error: (err) => this.notify.actionFailed('deleteExcuse failed', err),
    });
  }
}
