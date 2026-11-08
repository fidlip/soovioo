import { inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute } from '@angular/router';
import { Dialogs, type ListView } from '@nativescript/core';
import { LINKED_ITEM_GONE } from '@soovioo/shared';

/** Pages of the app a notification opens, each with the query parameter
 * naming its thing (the web's, see NotificationLinks on the API). The other
 * pages stay on the web. */
export const MOBILE_PAGES: Record<string, string> = {
  grades: 'grade',
  absences: 'absence',
  messages: 'conversation',
};

/** Where a notification of this page leads in the app: its page with the
 * thing, or the notifications when the app hasn't the page. `at` makes each
 * opening a navigation of its own, also to the same thing again. */
export function notificationRoute(
  page: string,
  targetId?: number | null,
): { commands: string[]; queryParams: Record<string, string | number> } {
  const name = MOBILE_PAGES[page];
  const at = Date.now();
  if (!name) {
    return { commands: ['/notifications'], queryParams: { at } };
  }
  return {
    commands: [`/${page}`],
    queryParams: targetId ? { [name]: targetId, at } : { at },
  };
}

/** `?<name>=<id>` — what a notification opens on the page. Calls `open`
 * each time one comes, also while the page is already open. Call from a
 * constructor. */
export function onLinkedItem(name: string, open: (id: number) => void): void {
  inject(ActivatedRoute)
    .queryParamMap.pipe(takeUntilDestroyed())
    .subscribe((params) => {
      const id = Number(params.get(name));
      if (id) {
        open(id);
      }
    });
}

const HIGHLIGHT_MS = 5000;

/** Styles of the marked row (the page's `styles`) — like the web's, it
 * blinks three times, stays, then fades. */
export const HIGHLIGHT_STYLES = `
.app-highlight {
  animation-name: app-highlight;
  animation-duration: ${HIGHLIGHT_MS}ms;
  animation-timing-function: ease-in-out;
}
@keyframes app-highlight {
  0% { background-color: transparent; }
  9% { background-color: #e0e7ff; }
  18% { background-color: transparent; }
  27% { background-color: #e0e7ff; }
  36% { background-color: transparent; }
  45% { background-color: #e0e7ff; }
  75% { background-color: #e0e7ff; }
  100% { background-color: transparent; }
}
`;

/** The row a notification opened, marked for a moment
 * (`HIGHLIGHT_STYLES`). */
export class Highlight {
  readonly id = signal<number | null>(null);
  private timer: ReturnType<typeof setTimeout> | undefined;

  /** Scrolls the list to the row and marks it; nothing when it isn't
   * there. */
  show(list: ListView | undefined, ids: number[], id: number): void {
    const index = ids.indexOf(id);
    if (index < 0) {
      return;
    }
    list?.scrollToIndex(index);
    clearTimeout(this.timer);
    // A new animation even when the same row is still marked.
    this.id.set(null);
    setTimeout(() => {
      this.id.set(id);
      this.timer = setTimeout(() => this.id.set(null), HIGHLIGHT_MS);
    });
  }
}

/** A notification's thing that's gone or no longer the user's to see. */
export function linkedItemGone(): void {
  Dialogs.alert({ message: LINKED_ITEM_GONE, okButtonText: 'OK' });
}
