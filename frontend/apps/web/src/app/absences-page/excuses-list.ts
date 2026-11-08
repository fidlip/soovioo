import {
  Component,
  computed,
  inject,
  input,
  output,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
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
} from '@soovioo/shared';
import { ClearFilters, FloatField } from '../forms';
import { Loading } from '../loading/loading';
import { NotifyService } from '../notify.service';
import { DecisionMenu } from './decision-menu';
import { excuseFileDownloader, formatFileSize } from './excuse-files';
import { RejectDialog } from './reject-dialog';
import { excuseRange, formatPeriods, todayString } from './excuse-days';

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
    ClearFilters,
    DecisionMenu,
    FloatField,
    Loading,
    FormsModule,
    Button,
    PrimeTemplate,
    RejectDialog,
    Select,
    TableModule,
    Tag,
    Tooltip,
  ],
  template: `
    <div class="mb-3 flex flex-wrap items-end gap-3">
      <app-float-field label="Žák" for="excuseFilterStudent">
        <p-select
          ariaLabel="Žák"
          inputId="excuseFilterStudent"
          [options]="studentOptions()"
          optionLabel="label"
          optionValue="id"
          [(ngModel)]="filterStudentId"
          [showClear]="true"
          name="excuseFilterStudent"
          appendTo="body"
          styleClass="w-52"
        />
      </app-float-field>
      <app-float-field label="Stav" for="excuseFilterStatus">
        <p-select
          ariaLabel="Stav"
          inputId="excuseFilterStatus"
          [options]="statusOptions"
          optionLabel="label"
          optionValue="value"
          [(ngModel)]="filterStatus"
          [showClear]="true"
          name="excuseFilterStatus"
          appendTo="body"
          styleClass="w-48"
        />
      </app-float-field>
      @if (filterStudentId !== null || filterStatus !== null) {
        <app-clear-filters (cleared)="clearFilters()" />
      }
    </div>

    <p-table [value]="filtered()" dataKey="id" styleClass="rounded-border">
      <ng-template pTemplate="header">
        <tr>
          <th>Žák</th>
          <th>Období</th>
          <th>Důvod</th>
          <th>Stav</th>
          <th><span class="sr-only">Akce</span></th>
        </tr>
      </ng-template>
      <ng-template pTemplate="body" let-excuse>
        <tr [id]="'excuse-' + excuse.id">
          <td>{{ excuse.studentName }}</td>
          <td class="whitespace-nowrap">
            {{ range(excuse) }}
            @if (excuse.periods.length > 0) {
              <div class="text-xs text-surface-500">
                {{ periods(excuse.periods) }} hodina
              </div>
            }
          </td>
          <td>
            <div>{{ excuse.reason }}</div>
            @for (file of excuse.attachments; track file.id) {
              <p-button
                [label]="file.fileName + ' (' + fileSize(file.sizeBytes) + ')'"
                icon="pi pi-paperclip"
                size="small"
                [link]="true"
                styleClass="px-0"
                (onClick)="download(excuse.id, file)"
              />
            }
            @if (excuse.createdByName) {
              <div class="text-xs text-surface-500">
                Napsal(a) {{ excuse.createdByName
                }}{{ excuse.channel ? ' · ' + channelLabel(excuse) : '' }}
              </div>
            }
          </td>
          <td>
            <p-tag
              [value]="statusLabel[status(excuse)]"
              [severity]="statusSeverity[status(excuse)]"
            />
            @if (mixed(excuse)) {
              <ul class="m-0 mt-1 list-none p-0 text-xs">
                @for (day of excuse.days; track day.absenceId) {
                  <li
                    [class]="
                      day.status === 'Rejected'
                        ? 'text-red-700 dark:text-red-300'
                        : ''
                    "
                  >
                    {{ dayText(day) }}
                  </li>
                }
              </ul>
            } @else {
              @for (note of notes(excuse); track note) {
                <div class="mt-1 text-xs text-red-700 dark:text-red-300">
                  {{ note }}
                </div>
              }
            }
          </td>
          <td>
            <div class="flex flex-wrap justify-end gap-2">
              @if (canReview() && waiting(excuse)) {
                <span
                  [pTooltip]="
                    approvable(excuse)
                      ? ''
                      : 'Schválit jde nejdříve v den absence.'
                  "
                >
                  <p-button
                    [label]="
                      mixed(excuse) ? 'Schválit zbývající dny' : 'Schválit'
                    "
                    icon="pi pi-check"
                    size="small"
                    severity="success"
                    [disabled]="!approvable(excuse)"
                    (onClick)="approve(excuse)"
                  />
                </span>
                <p-button
                  [label]="
                    mixed(excuse) ? 'Zamítnout zbývající dny' : 'Zamítnout'
                  "
                  icon="pi pi-times"
                  size="small"
                  severity="danger"
                  [outlined]="true"
                  (onClick)="reject(excuse)"
                />
              }
              @if (canReview() && !waiting(excuse)) {
                <app-decision-menu
                  [approved]="has(excuse, 'Excused')"
                  [rejected]="has(excuse, 'Rejected')"
                  [approvable]="approvable(excuse)"
                  (approve)="approve(excuse)"
                  (reject)="reject(excuse)"
                  (reopen)="reopen(excuse)"
                />
              }
              @if (canReview() || (isFamily() && waiting(excuse))) {
                <p-button
                  icon="pi pi-pencil"
                  ariaLabel="Upravit"
                  size="small"
                  [text]="true"
                  (onClick)="edit.emit(excuse)"
                />
                <p-button
                  icon="pi pi-trash"
                  ariaLabel="Smazat"
                  size="small"
                  severity="danger"
                  [text]="true"
                  (onClick)="remove(excuse)"
                />
              }
            </div>
          </td>
        </tr>
      </ng-template>
      <ng-template pTemplate="emptymessage">
        <tr>
          <td colspan="5" class="py-6 text-center text-surface-500">
            @if (excuses()) {
              Žádné omluvenky.
            } @else {
              <app-loading class="justify-center" />
            }
          </td>
        </tr>
      </ng-template>
    </p-table>

    <app-reject-dialog />
  `,
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
      seen.set(e.studentId, e.studentName);
    }
    return [...seen].map(([id, label]) => ({ id, label }));
  });
  protected readonly fileSize = formatFileSize;
  protected readonly periods = formatPeriods;
  protected readonly range = excuseRange;

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
      `Zamítnout omluvenku — ${excuse.studentName}`,
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
