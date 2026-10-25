import { Component, effect, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import {
  LeaveNotice,
  LeaveNoticeGroup,
  RetentionApiService,
  StudentLeaveReason,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import { LEAVE_REASON_OPTIONS, toDateOnly } from './retention-labels';

/** The pupil whose departure is edited — a Student or a FormerStudent. */
export interface LeaveTarget {
  id: number;
  firstName: string;
  lastName: string;
  leftAt?: string | null;
  reason?: StudentLeaveReason | null;
  reasonDetail?: string | null;
  note?: string | null;
}

interface NoticeRow {
  group: LeaveNoticeGroup;
  label: string;
  notify: boolean;
  withReason: boolean;
}

/** Administrace → Studenti / Archiv a výmaz údajů: records a pupil's
 * departure (or takes it back) and tells whom the admin picks. Open while
 * `student` is set. */
@Component({
  selector: 'app-leave-dialog',
  imports: [
    FormsModule,
    Button,
    Checkbox,
    DatePicker,
    Dialog,
    InputText,
    Select,
    TermPipe,
    ...FORM_FIELDS,
  ],
  template: `
    <p-dialog
      [visible]="student() !== null"
      (visibleChange)="!$event && closed.emit()"
      [header]="'Ukončení vzdělávání — ' + name()"
      [modal]="true"
      styleClass="w-full max-w-lg"
    >
      <form class="flex flex-col gap-4" #leaveForm="ngForm" (ngSubmit)="save()">
        <p class="m-0 text-sm text-surface-500">
          {{
            'Od následujícího dne se žák v aplikaci nezobrazuje a jeho rodiče k němu ztratí přístup. Údaje zůstanou v Administraci → Archiv a výmaz údajů a mažou se podle pravidel školy.'
              | term
          }}
        </p>
        <div class="flex flex-col gap-1">
          <label for="leave-date">Poslední den ve škole</label>
          <p-datePicker
            inputId="leave-date"
            name="leaveDate"
            required
            [(ngModel)]="leftAt"
            [showIcon]="true"
            [showOnFocus]="false"
            appendTo="body"
          />
        </div>
        <div class="flex flex-col gap-1">
          <label for="leave-reason">Důvod</label>
          <p-select
            inputId="leave-reason"
            name="leaveReason"
            [options]="reasons"
            optionLabel="label"
            optionValue="value"
            [(ngModel)]="reason"
            [showClear]="true"
            placeholder="Neuvedeno"
            appendTo="body"
          />
        </div>
        @if (reason === 'Other') {
          <div class="flex flex-col gap-1">
            <label for="leave-reason-detail">Odůvodnění</label>
            <input
              pInputText
              id="leave-reason-detail"
              name="reasonDetail"
              required
              [(ngModel)]="reasonDetail"
            />
          </div>
        }
        <div class="flex flex-col gap-1">
          <label for="leave-note">Poznámka (jen pro školu)</label>
          <input
            pInputText
            id="leave-note"
            name="leaveNote"
            [(ngModel)]="note"
            placeholder="Např. kam přestupuje"
          />
        </div>
        <fieldset class="m-0 flex flex-col gap-2 border-0 p-0">
          <legend class="mb-2 text-sm font-medium">
            Oznámit zprávou
            <span class="font-normal text-surface-500"
              >(při zadání, změně data nebo zrušení)</span
            >
          </legend>
          @for (row of notices; track row.group) {
            <div class="flex flex-wrap items-center gap-x-6 gap-y-1">
              <div class="flex w-40 items-center gap-2">
                <p-checkbox
                  [inputId]="'notify-' + row.group"
                  [name]="'notify-' + row.group"
                  [binary]="true"
                  [(ngModel)]="row.notify"
                />
                <label [for]="'notify-' + row.group">{{
                  row.label | term
                }}</label>
              </div>
              <div class="flex items-center gap-2">
                <p-checkbox
                  [inputId]="'reason-' + row.group"
                  [name]="'reason-' + row.group"
                  [binary]="true"
                  [disabled]="!row.notify || !reason"
                  [(ngModel)]="row.withReason"
                />
                <label
                  [for]="'reason-' + row.group"
                  class="text-sm text-surface-600 dark:text-surface-300"
                  >i s důvodem</label
                >
              </div>
            </div>
          }
        </fieldset>
        <div class="flex flex-wrap justify-between gap-2">
          @if (student()?.leftAt) {
            <p-button
              label="Zrušit ukončení"
              severity="secondary"
              [outlined]="true"
              (onClick)="cancel()"
            />
          } @else {
            <span></span>
          }
          <app-submit-button [form]="leaveForm" label="Uložit" />
        </div>
      </form>
    </p-dialog>
  `,
})
export class LeaveDialog {
  private readonly retentionApi = inject(RetentionApiService);
  private readonly notify = inject(NotifyService);

  readonly student = input<LeaveTarget | null>(null);
  readonly closed = output<void>();
  /** The departure was saved or taken back. */
  readonly changed = output<void>();

  protected readonly reasons = LEAVE_REASON_OPTIONS;
  protected leftAt: Date | null = null;
  protected reason: StudentLeaveReason | null = null;
  protected reasonDetail = '';
  protected note = '';
  protected notices: NoticeRow[] = [];

  constructor() {
    effect(() => {
      const student = this.student();
      this.leftAt = student?.leftAt ? new Date(student.leftAt) : null;
      this.reason = student?.reason ?? null;
      this.reasonDetail = student?.reasonDetail ?? '';
      this.note = student?.note ?? '';
      // Everyone hears of it; the reason only the class teacher by default.
      this.notices = [
        { group: 'Student', label: 'Žák', notify: true, withReason: false },
        { group: 'Parents', label: 'Rodiče', notify: true, withReason: false },
        {
          group: 'ClassTeacher',
          label: 'Třídní učitel',
          notify: true,
          withReason: true,
        },
      ];
    });
  }

  protected name(): string {
    const s = this.student();
    return s ? `${s.firstName} ${s.lastName}` : '';
  }

  private chosenNotices(): LeaveNotice[] {
    return this.notices
      .filter((n) => n.notify)
      .map((n) => ({
        group: n.group,
        withReason: n.withReason && this.reason !== null,
      }));
  }

  protected save(): void {
    const student = this.student();
    if (!student || !this.leftAt) {
      return;
    }
    this.retentionApi
      .setLeave(student.id, {
        leftAt: toDateOnly(this.leftAt),
        reason: this.reason,
        reasonDetail:
          this.reason === 'Other' ? this.reasonDetail.trim() || null : null,
        note: this.note.trim() || null,
        notices: this.chosenNotices(),
      })
      .subscribe({
        next: () => {
          this.notify.success('Ukončení vzdělávání uloženo.');
          this.changed.emit();
        },
        error: (err) => this.notify.actionFailed('setLeave failed', err),
      });
  }

  protected cancel(): void {
    const student = this.student();
    if (!student) {
      return;
    }
    this.retentionApi.cancelLeave(student.id, this.chosenNotices()).subscribe({
      next: () => {
        this.notify.success('Ukončení vzdělávání zrušeno.');
        this.changed.emit();
      },
      error: (err) => this.notify.actionFailed('cancelLeave failed', err),
    });
  }
}
