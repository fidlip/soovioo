import { DestroyRef, Signal, inject, signal } from '@angular/core';

/** Whether the CSS media query matches, kept up to date. Call in an injection
 * context. */
export function mediaQuery(query: string): Signal<boolean> {
  const media =
    typeof window !== 'undefined' && window.matchMedia
      ? window.matchMedia(query)
      : null;
  const matches = signal(media?.matches ?? false);
  if (media) {
    const listener = (e: MediaQueryListEvent) => matches.set(e.matches);
    media.addEventListener('change', listener);
    inject(DestroyRef).onDestroy(() =>
      media.removeEventListener('change', listener),
    );
  }
  return matches.asReadonly();
}

/** Tailwind's md: from here on it isn't a phone. */
export const DESKTOP_QUERY = '(min-width: 768px)';
