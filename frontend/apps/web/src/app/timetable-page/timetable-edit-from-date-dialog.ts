import { Component, effect, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import {
  TimetableVersion,
  TimetableVersionsApiService,
  nextMonday,
} from '@soovioo/shared';
import { toDateOnly } from '../departures/retention-labels';
import { FORM_FIELDS, FloatField } from '../forms';
import { NotifyService } from '../notify.service';

/** Upravit od data: the day a change to the version that applies starts on.
 * Open while `version` is set. */
@Component({
  selector: 'app-timetable-edit-from-date-dialog',
  imports: [...FORM_FIELDS, DatePicker, Dialog, FloatField, FormsModule],
  templateUrl: './timetable-edit-from-date-dialog.html',
})
export class TimetableEditFromDateDialog {
  private readonly versionsApi = inject(TimetableVersionsApiService);
  private readonly notify = inject(NotifyService);

  readonly version = input<TimetableVersion | null>(null);
  readonly closed = output<void>();
  /** The id of the draft to work on. */
  readonly opened = output<number>();

  protected validFrom: Date | null = null;
  protected readonly minDate = new Date(new Date().setHours(0, 0, 0, 0));

  constructor() {
    effect(() => {
      if (this.version()) {
        this.validFrom = nextMonday(new Date());
      }
    });
  }

  protected save(): void {
    const version = this.version();
    if (!version || !this.validFrom) {
      return;
    }
    this.versionsApi
      .editFromDate(version.id, toDateOnly(this.validFrom))
      .subscribe({
        next: (draft) => this.opened.emit(draft.id),
        error: (err) => this.notify.actionFailed('editFromDate failed', err),
      });
  }
}
