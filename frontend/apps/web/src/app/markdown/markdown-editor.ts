import { Component, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Textarea } from '@openng/optimus-ui/textarea';
import { FloatField } from '../forms';
import { Markdown } from './markdown';

/** A text field written in Markdown: "Napsat" edits, "Náhled" shows it
 * rendered. A form control — `ngModel`, `required`, `maxlength` work on it
 * as on a textarea. */
@Component({
  selector: 'app-markdown-editor',
  imports: [FloatField, Markdown, Textarea],
  templateUrl: './markdown-editor.html',
  host: { class: 'block' },
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: MarkdownEditor, multi: true },
  ],
})
export class MarkdownEditor implements ControlValueAccessor {
  readonly label = input.required<string>();
  readonly inputId = input.required<string>();
  readonly rows = input(5);
  readonly maxlength = input<number | string | null>(null);

  protected readonly preview = signal(false);
  protected readonly value = signal('');
  protected readonly disabled = signal(false);
  private onChange: (value: string) => void = () => undefined;
  protected onTouched: () => void = () => undefined;

  writeValue(value: string | null): void {
    this.value.set(value ?? '');
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(disabled: boolean): void {
    this.disabled.set(disabled);
  }

  protected input(value: string): void {
    this.value.set(value);
    this.onChange(value);
  }
}
