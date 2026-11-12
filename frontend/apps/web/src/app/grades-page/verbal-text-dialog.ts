import { Component, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Select } from '@openng/optimus-ui/select';
import { Textarea } from '@openng/optimus-ui/textarea';
import { ReportCardResult } from '@soovioo/shared';
import { FloatField, FORM_FIELDS } from '../forms';

export interface VerbalTextDialogOptions {
  header: string;
  text: string | null;
  /** The souhrnné hodnocení: the overall result goes with the text. */
  results?: { value: ReportCardResult; label: string }[];
  result?: ReportCardResult | null;
  save: (text: string, result: ReportCardResult | null) => void;
}

/** Writes a slovní hodnocení — of a subject, behaviour, or the souhrnné
 * hodnocení with its overall result. An empty text removes it. */
@Component({
  selector: 'app-verbal-text-dialog',
  imports: [Dialog, FloatField, ...FORM_FIELDS, FormsModule, Select, Textarea],
  templateUrl: './verbal-text-dialog.html',
})
export class VerbalTextDialog {
  protected readonly visible = signal(false);
  protected readonly options = signal<VerbalTextDialogOptions | null>(null);
  protected text = '';
  protected result: ReportCardResult | null = null;

  open(options: VerbalTextDialogOptions): void {
    this.options.set(options);
    this.text = options.text ?? '';
    this.result = options.result ?? null;
    this.visible.set(true);
  }

  protected save(): void {
    const options = this.options();
    if (!options) {
      return;
    }
    this.visible.set(false);
    const text = this.text.trim();
    options.save(text, text ? this.result : null);
  }
}
