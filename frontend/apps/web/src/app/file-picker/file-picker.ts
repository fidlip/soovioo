import { Component, ElementRef, input, output, viewChild } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';

/** Optimus-styled replacement for a bare `<input type="file">`: a button
 * opens the native picker (kept hidden), the chosen file's name shows next
 * to it with a clear button. The parent owns the File — `fileName` is what
 * it currently holds, `fileSelected` passes the native change event through
 * unchanged (existing size/type checks keep working), `cleared` asks it to
 * drop the file. */
@Component({
  selector: 'app-file-picker',
  imports: [Button],
  template: `
    <input
      #input
      type="file"
      class="hidden"
      [id]="inputId()"
      [attr.aria-label]="label()"
      (change)="fileSelected.emit($event)"
    />
    <p-button
      type="button"
      [label]="label()"
      icon="pi pi-paperclip"
      severity="secondary"
      [outlined]="true"
      size="small"
      (onClick)="input.click()"
    />
    @if (fileName(); as name) {
    <span class="min-w-0 truncate text-sm text-surface-700 dark:text-surface-200">{{
      name
    }}</span>
    <p-button
      type="button"
      icon="pi pi-times"
      severity="secondary"
      [text]="true"
      [rounded]="true"
      size="small"
      ariaLabel="Odebrat přílohu"
      (onClick)="clear()"
    />
    }
  `,
  host: { class: 'flex min-w-0 items-center gap-2' },
})
export class FilePicker {
  readonly inputId = input<string | undefined>(undefined);
  readonly label = input('Přiložit soubor');
  readonly fileName = input<string | null | undefined>(null);
  readonly fileSelected = output<Event>();
  readonly cleared = output<void>();

  private readonly input = viewChild.required<ElementRef<HTMLInputElement>>('input');

  protected clear(): void {
    this.input().nativeElement.value = '';
    this.cleared.emit();
  }
}
