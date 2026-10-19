import { DOCUMENT } from '@angular/common';
import { DestroyRef, inject } from '@angular/core';

/** App-wide: hovering an empty input/textarea (or a select still showing its
 * placeholder) shows the full placeholder as a native tooltip, so a long
 * placeholder cut off by a narrow field stays readable. Installed once from
 * the root component — no per-field markup needed. */
export function installPlaceholderTitles(): void {
  const document = inject(DOCUMENT);
  const listener = (event: Event) => {
    const target = event.target as HTMLElement | null;
    if (!target) {
      return;
    }
    if (
      target instanceof HTMLInputElement ||
      target instanceof HTMLTextAreaElement
    ) {
      const placeholder = target.placeholder;
      if (placeholder && !target.value) {
        target.title = placeholder;
      } else if (target.title === placeholder) {
        target.removeAttribute('title');
      }
      return;
    }
    const selectPlaceholder = target.closest<HTMLElement>('.p-placeholder');
    if (selectPlaceholder && !selectPlaceholder.title) {
      selectPlaceholder.title = selectPlaceholder.textContent?.trim() ?? '';
    }
  };
  document.addEventListener('mouseover', listener, { passive: true });
  inject(DestroyRef).onDestroy(() =>
    document.removeEventListener('mouseover', listener),
  );
}
