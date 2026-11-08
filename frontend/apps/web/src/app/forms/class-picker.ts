import { Component, computed, input, model, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { InputText } from '@openng/optimus-ui/inputtext';
import { foldForSearch } from '../text-search';
import { FloatField } from './float-field';

export interface ClassPickerOption {
  id: number;
  name: string;
}

/** Picks several of the school's classes: a searchable list of checkboxes,
 * all classes in view at once. */
@Component({
  selector: 'app-class-picker',
  imports: [Checkbox, FloatField, FormsModule, InputText],
  template: `
    <div class="flex flex-col gap-1.5" role="group" [attr.aria-label]="label()">
      <span
        class="text-sm font-medium text-surface-600 dark:text-surface-300"
        >{{ label() }}</span
      >
      <app-float-field label="Hledat třídu" [for]="inputId()">
        <input
          [id]="inputId()"
          pInputText
          type="text"
          class="max-w-40"
          [ngModel]="query()"
          (ngModelChange)="query.set($event)"
          [ngModelOptions]="{ standalone: true }"
        />
      </app-float-field>
      <div
        class="flex max-h-32 flex-col gap-1 overflow-y-auto rounded border border-surface-200 p-2 dark:border-surface-700"
      >
        @for (c of shown(); track c.id) {
          <label class="flex items-center gap-2 text-sm">
            <p-checkbox
              [binary]="true"
              [ngModel]="selected().includes(c.id)"
              (ngModelChange)="toggle(c.id)"
              [ngModelOptions]="{ standalone: true }"
            />
            {{ c.name }}
          </label>
        } @empty {
          <span class="text-sm text-surface-500 dark:text-surface-400"
            >Nic nenalezeno.</span
          >
        }
      </div>
    </div>
  `,
  host: { class: 'block' },
})
export class ClassPicker {
  readonly classes = input.required<readonly ClassPickerOption[]>();
  readonly selected = model<number[]>([]);
  readonly label = input('Třídy');
  /** Id of the search field. */
  readonly inputId = input.required<string>();

  protected readonly query = signal('');
  protected readonly shown = computed(() => {
    const query = foldForSearch(this.query().trim());
    return this.classes().filter(
      (c) => !query || foldForSearch(c.name).includes(query),
    );
  });

  protected toggle(id: number): void {
    const ids = this.selected();
    this.selected.set(
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
  }
}
