import {
  Component,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import {
  HandoverConflict,
  RetentionApiService,
  StaffLeaveNoticeGroup,
  StaffLeavePreview,
  StaffLeaveReason,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { NotifyService } from '../notify.service';
import {
  DAY_LABELS,
  STAFF_LEAVE_NOTICE_LABELS,
  STAFF_LEAVE_REASON_OPTIONS,
  toDateOnly,
} from './retention-labels';

/** The member of staff whose departure is edited. */
export interface StaffLeaveTarget {
  userId: number;
  name: string;
}

/** Ukončení pracovního poměru: the last day, the reason and a note for the
 * school only, who takes over třídnictví and lessons (with the timetable
 * clashes that would cause), and whom to tell. Open while `staff` is set. */
@Component({
  selector: 'app-staff-leave-dialog',
  imports: [
    FormsModule,
    Button,
    Checkbox,
    DatePicker,
    Dialog,
    InputText,
    Message,
    Select,
    ...FORM_FIELDS,
  ],
  templateUrl: './staff-leave-dialog.html',
})
export class StaffLeaveDialog {
  private readonly retentionApi = inject(RetentionApiService);
  private readonly notify = inject(NotifyService);

  readonly staff = input<StaffLeaveTarget | null>(null);
  readonly closed = output<void>();
  /** The departure was saved or taken back. */
  readonly changed = output<void>();

  protected readonly reasons = STAFF_LEAVE_REASON_OPTIONS;
  protected readonly days = DAY_LABELS;
  protected readonly noticeGroups = (
    Object.keys(STAFF_LEAVE_NOTICE_LABELS) as StaffLeaveNoticeGroup[]
  ).map((group) => ({ group, label: STAFF_LEAVE_NOTICE_LABELS[group] }));

  protected readonly preview = signal<StaffLeavePreview | null>(null);
  protected readonly conflicts = signal<HandoverConflict[]>([]);
  protected lastDay: Date | null = null;
  protected reason: StaffLeaveReason | null = null;
  protected note = '';
  /** Class id → successor, subject id → substitute; null = not handed over. */
  protected successors: Record<number, number | null> = {};
  protected substitutes: Record<number, number | null> = {};
  protected notices: Record<StaffLeaveNoticeGroup, boolean> = {
    LedClasses: false,
    TaughtClasses: false,
    Colleagues: false,
  };
  protected readonly minDate = new Date(new Date().setHours(0, 0, 0, 0));

  constructor() {
    effect(() => {
      const staff = this.staff();
      this.preview.set(null);
      this.conflicts.set([]);
      if (!staff) {
        return;
      }
      this.retentionApi.getStaffLeave(staff.userId).subscribe({
        next: (preview) => this.load(preview),
        error: (err) => this.notify.actionFailed('getStaffLeave failed', err),
      });
    });
  }

  private load(preview: StaffLeavePreview): void {
    this.lastDay = preview.lastDay ? new Date(preview.lastDay) : null;
    this.reason = preview.reason;
    this.note = preview.note ?? '';
    this.successors = Object.fromEntries(
      preview.ledClasses.map((c) => [c.classId, c.successorId]),
    );
    this.substitutes = Object.fromEntries(
      preview.subjects.map((s) => [s.subjectId, s.substituteId]),
    );
    const defaults = preview.noticeDefaults ?? [];
    this.notices = {
      LedClasses: defaults.includes('LedClasses'),
      TaughtClasses: defaults.includes('TaughtClasses'),
      Colleagues: defaults.includes('Colleagues'),
    };
    this.preview.set(preview);
    this.checkConflicts();
  }

  protected staffOptions(): { label: string; value: number }[] {
    return (this.preview()?.staff ?? []).map((s) => ({
      label: s.name,
      value: s.userId,
    }));
  }

  /** Asks again whenever a substitute changes. */
  protected checkConflicts(): void {
    const staff = this.staff();
    const chosen = this.chosenSubstitutes();
    if (!staff || chosen.length === 0) {
      this.conflicts.set([]);
      return;
    }
    this.retentionApi.getHandoverConflicts(staff.userId, chosen).subscribe({
      next: (conflicts) => this.conflicts.set(conflicts),
      error: (err) =>
        this.notify.actionFailed('getHandoverConflicts failed', err),
    });
  }

  protected subjectName(subjectId: number): string {
    return (
      this.preview()?.subjects.find((s) => s.subjectId === subjectId)?.name ??
      ''
    );
  }

  /** Třídnictví and subjects left without anyone. */
  protected notHandedOver(): number {
    return (
      Object.values(this.successors).filter((id) => id === null).length +
      Object.values(this.substitutes).filter((id) => id === null).length
    );
  }

  private chosenSubstitutes() {
    return Object.entries(this.substitutes)
      .filter(([, id]) => id !== null)
      .map(([subjectId, id]) => ({
        subjectId: Number(subjectId),
        substituteId: id as number,
      }));
  }

  private chosenNotices(): StaffLeaveNoticeGroup[] {
    return this.noticeGroups
      .map((n) => n.group)
      .filter((group) => this.notices[group]);
  }

  protected save(): void {
    const staff = this.staff();
    if (!staff || !this.lastDay) {
      return;
    }
    this.retentionApi
      .setStaffLeave(staff.userId, {
        lastDay: toDateOnly(this.lastDay),
        reason: this.reason,
        note: this.note.trim() || null,
        classTeachers: Object.entries(this.successors)
          .filter(([, id]) => id !== null)
          .map(([classId, id]) => ({
            classId: Number(classId),
            successorId: id as number,
          })),
        substitutes: this.chosenSubstitutes(),
        notices: this.chosenNotices(),
      })
      .subscribe({
        next: () => {
          this.notify.success('Ukončení pracovního poměru uloženo.');
          this.changed.emit();
        },
        error: (err) => this.notify.actionFailed('setStaffLeave failed', err),
      });
  }

  protected cancel(): void {
    const staff = this.staff();
    if (!staff) {
      return;
    }
    this.retentionApi
      .cancelStaffLeave(staff.userId, this.chosenNotices())
      .subscribe({
        next: () => {
          this.notify.success('Ukončení pracovního poměru zrušeno.');
          this.changed.emit();
        },
        error: (err) =>
          this.notify.actionFailed('cancelStaffLeave failed', err),
      });
  }
}
