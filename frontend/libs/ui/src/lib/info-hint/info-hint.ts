import { Component, input } from '@angular/core';
import { Tooltip } from '@openng/optimus-ui/tooltip';

/** `(i)` next to a section heading — the explanatory text shows in a tooltip
 * on hover (desktop) or tap/focus (mobile, keyboard) instead of taking up
 * a paragraph under the heading. */
@Component({
  selector: 'app-info-hint',
  imports: [Tooltip],
  template: `<i
    class="pi pi-info-circle cursor-help align-middle text-base font-normal text-surface-500 dark:text-surface-400 hover:text-primary"
    tabindex="0"
    role="note"
    [attr.aria-label]="text()"
    [pTooltip]="text()"
    tooltipPosition="top"
    tooltipEvent="both"
  ></i>`,
  host: { class: 'ml-1.5 inline-flex' },
})
export class InfoHint {
  readonly text = input.required<string>();
}
