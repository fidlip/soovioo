import { Component, effect, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { RetentionApiService, StudentLeaveReason } from '@soovioo/shared';
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
  note?: string | null;
}

/** Administrace → Studenti / Archiv a výmaz údajů: records a pupil's departure (or takes it
 * back). Open while `student` is set. */
@Component({
  selector: 'app-leave-dialog',
  imports: [
    FormsModule,
    Button,
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
            dateFormat="dd.mm.yy"
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
        <div class="flex flex-col gap-1">
          <label for="leave-note">Poznámka</label>
          <input
            pInputText
            id="leave-note"
            name="leaveNote"
            [(ngModel)]="note"
            placeholder="Např. kam přestupuje"
          />
        </div>
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
  protected note = '';

  constructor() {
    effect(() => {
      const student = this.student();
      this.leftAt = student?.leftAt ? new Date(student.leftAt) : null;
      this.reason = student?.reason ?? null;
      this.note = student?.note ?? '';
    });
  }

  protected name(): string {
    const s = this.student();
    return s ? `${s.firstName} ${s.lastName}` : '';
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
        note: this.note.trim() || null,
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
    this.retentionApi.cancelLeave(student.id).subscribe({
      next: () => {
        this.notify.success('Ukončení vzdělávání zrušeno.');
        this.changed.emit();
      },
      error: (err) => this.notify.actionFailed('cancelLeave failed', err),
    });
  }
}
