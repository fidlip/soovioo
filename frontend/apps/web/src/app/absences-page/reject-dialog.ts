import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Textarea } from '@openng/optimus-ui/textarea';
import { FloatField, FORM_FIELDS } from '../forms';

/** Asks why an omluvenka (or a day of it) is rejected — the family sees
 * the reason. */
@Component({
  selector: 'app-reject-dialog',
  imports: [FloatField, ...FORM_FIELDS, FormsModule, Dialog, Textarea],
  template: `
    <p-dialog
      [visible]="visible()"
      (visibleChange)="visible.set($event)"
      [header]="header()"
      [modal]="true"
      styleClass="w-full max-w-md"
    >
      <form
        class="flex flex-col gap-3"
        #rejectForm="ngForm"
        (ngSubmit)="confirm()"
      >
        <app-float-field label="Zdůvodnění" for="rejectNote">
          <textarea
            id="rejectNote"
            pTextarea
            rows="3"
            name="note"
            required
            maxlength="500"
            [(ngModel)]="note"
          ></textarea>
        </app-float-field>
        <app-submit-button
          [form]="rejectForm"
          label="Zamítnout"
          icon="pi pi-times"
          severity="danger"
          class="self-start"
        />
      </form>
    </p-dialog>
  `,
})
export class RejectDialog {
  protected readonly visible = signal(false);
  protected readonly header = signal('');
  protected note = '';
  private onConfirm: (note: string) => void = () => undefined;

  open(header: string, onConfirm: (note: string) => void): void {
    this.header.set(header);
    this.note = '';
    this.onConfirm = onConfirm;
    this.visible.set(true);
  }

  protected confirm(): void {
    const note = this.note.trim();
    if (!note) {
      return;
    }
    this.visible.set(false);
    this.onConfirm(note);
  }
}
