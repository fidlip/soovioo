import { Component, computed, input, output } from '@angular/core';
import { MenuItem } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Menu } from '@openng/optimus-ui/menu';

/** "Změnit rozhodnutí" of a decided omluvenka or day: the other decision,
 * or back to waiting. */
@Component({
  selector: 'app-decision-menu',
  imports: [Button, Menu],
  template: `
    <p-button
      label="Změnit rozhodnutí"
      icon="pi pi-angle-down"
      iconPos="right"
      size="small"
      severity="secondary"
      [outlined]="true"
      (onClick)="menu.toggle($event)"
    />
    <p-menu #menu [popup]="true" [model]="items()" appendTo="body" />
  `,
})
export class DecisionMenu {
  /** Some day of it is approved / rejected. */
  readonly approved = input(false);
  readonly rejected = input(false);
  /** A not-yet-approved day of it has come. */
  readonly approvable = input(false);
  readonly approve = output<void>();
  readonly reject = output<void>();
  readonly reopen = output<void>();

  protected readonly items = computed<MenuItem[]>(() => [
    ...(this.rejected()
      ? [
          {
            label: 'Schválit',
            icon: 'pi pi-check',
            disabled: !this.approvable(),
            title: this.approvable()
              ? undefined
              : 'Schválit jde nejdříve v den absence.',
            command: () => this.approve.emit(),
          },
        ]
      : []),
    ...(this.approved()
      ? [
          {
            label: 'Zamítnout',
            icon: 'pi pi-times',
            command: () => this.reject.emit(),
          },
        ]
      : []),
    {
      label: 'Vrátit k posouzení',
      icon: 'pi pi-undo',
      command: () => this.reopen.emit(),
    },
  ]);
}
