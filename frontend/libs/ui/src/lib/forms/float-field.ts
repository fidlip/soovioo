import { Component, input } from '@angular/core';
import { FloatLabel } from '@openng/optimus-ui/floatlabel';

/** A form field with its label floating on the border (optimus FloatLabel,
 * variant "on") — the label stays visible once the field has a value. `for`
 * is the control's `id` (`inputId` on optimus components); the control
 * needs no placeholder, which would keep the label floated. The control
 * stretches to the field's width, as it would in a flex column. */
@Component({
  selector: 'app-float-field',
  imports: [FloatLabel],
  template: `<p-floatlabel variant="on">
    <ng-content />
    <label [for]="for()">{{ label() }}</label>
  </p-floatlabel>`,
  styles: `
    p-floatlabel {
      display: flex;
      flex-direction: column;
    }
    label {
      max-width: calc(100% - 1.5rem);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }
  `,
  host: { class: 'block' },
})
export class FloatField {
  readonly label = input.required<string>();
  readonly for = input.required<string>();
}
