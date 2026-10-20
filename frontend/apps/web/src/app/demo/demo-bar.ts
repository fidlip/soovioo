import { Component, computed, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Menu } from '@openng/optimus-ui/menu';
import { SchoolContextService } from '../school-context';
import { DemoActionsService } from './demo-actions.service';
import { DemoFeedbackService } from './demo-feedback.service';

const DAY_MS = 24 * 60 * 60 * 1000;

/** Strip on top of every page of a demo school (like the support-mode
 * one): how long the demo lives, feedback, switching users, the "…" menu. */
@Component({
  selector: 'app-demo-bar',
  imports: [Menu, RouterLink],
  template: `
    <div
      class="mb-4 flex flex-wrap items-center justify-between gap-2 rounded-md border border-sky-300 bg-sky-50 px-3 py-2 text-sm text-sky-900 dark:border-sky-700 dark:bg-sky-950 dark:text-sky-100"
      role="status"
    >
      <span
        ><i class="pi pi-sparkles mr-1"></i><strong>Ukázková škola</strong> —
        {{ expiryText() }}</span
      >
      <span class="flex flex-wrap items-center gap-1.5">
        <button type="button" class="demo-bar-button" (click)="feedback.show()">
          <i class="pi pi-comment mr-1"></i>Zpětná vazba
        </button>
        <a class="demo-bar-button" [routerLink]="schoolContext.link('login')">
          <i class="pi pi-users mr-1"></i>Přepnout uživatele
        </a>
        <button
          type="button"
          class="demo-bar-button"
          aria-label="Možnosti dema"
          (click)="menu.toggle($event)"
        >
          <i class="pi pi-ellipsis-h"></i>
        </button>
        <p-menu #menu [popup]="true" [model]="menuItems()" appendTo="body" />
      </span>
    </div>
  `,
})
export class DemoBar {
  protected readonly schoolContext = inject(SchoolContextService);
  protected readonly feedback = inject(DemoFeedbackService);
  private readonly actions = inject(DemoActionsService);

  readonly slug = input.required<string>();
  readonly expiresAt = input<string | null>(null);

  protected readonly menuItems = computed(() => this.actions.menu(this.slug()));

  protected readonly expiryText = computed(() => {
    const expires = this.expiresAt();
    if (!expires) {
      return 'data se po čase smažou';
    }
    const days = Math.max(0, Math.ceil((new Date(expires).getTime() - Date.now()) / DAY_MS));
    const date = new Date(expires).toLocaleDateString('cs-CZ');
    if (days <= 1) {
      return `data budou smazána zítra (${date})`;
    }
    const unit = days < 5 ? 'dny' : 'dní';
    return `data budou smazána za ${days} ${unit} (${date})`;
  });
}
