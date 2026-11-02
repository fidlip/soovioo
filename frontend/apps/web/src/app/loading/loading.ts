import { Component, input } from '@angular/core';

/** "Načítání…" with a spinner, shown until a page's data arrives — instead
 * of its empty message, which would claim there's nothing. */
@Component({
  selector: 'app-loading',
  template: `<i class="pi pi-spin pi-spinner" aria-hidden="true"></i
    >{{ text() }}`,
  host: {
    class: 'flex items-center gap-2 text-surface-500',
    role: 'status',
  },
})
export class Loading {
  readonly text = input('Načítání…');
}
