import { Component, output } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';

@Component({
  selector: 'app-clear-filters',
  imports: [Button],
  template: `
    <p-button
      label="Zrušit filtry"
      icon="pi pi-filter-slash"
      size="small"
      severity="secondary"
      [text]="true"
      (onClick)="cleared.emit()"
    />
  `,
})
export class ClearFilters {
  readonly cleared = output<void>();
}
