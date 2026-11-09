import { inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';

const WAIT_MS = 5000;

/** `?<name>=<id>` — what a notification's link opens on the page (see
 * NotificationLinks on the API). Calls `open` with the id each time one
 * comes, also while the page is already open, and drops the address's
 * query (but for `keep`, e.g. the page's own section) so a reload doesn't
 * open it again and the same link works twice. Call from a constructor. */
export function onLinkedItem(
  name: string,
  open: (id: number, params: ParamMap) => void,
  keep: string[] = [],
): void {
  const route = inject(ActivatedRoute);
  const router = inject(Router);
  route.queryParamMap.pipe(takeUntilDestroyed()).subscribe((params) => {
    const id = Number(params.get(name));
    if (!id) {
      return;
    }
    router.navigate([], {
      relativeTo: route,
      queryParams: Object.fromEntries(keep.map((k) => [k, params.get(k)])),
      replaceUrl: true,
    });
    open(id, params);
  });
}

/** Scrolls to the element with this id once it's there (the list may still
 * be loading) and marks it for a moment. Gives up after a few seconds —
 * the thing may be gone or filtered out. */
export function highlightElement(id: string): void {
  const started = performance.now();
  const step = () => {
    const element = document.getElementById(id);
    if (!element) {
      if (performance.now() - started < WAIT_MS) {
        requestAnimationFrame(step);
      }
      return;
    }
    const reduced = window.matchMedia?.(
      '(prefers-reduced-motion: reduce)',
    ).matches;
    element.scrollIntoView({
      block: 'center',
      behavior: reduced ? 'auto' : 'smooth',
    });
    element.classList.remove('app-highlight');
    // A new animation even when the same one is still running.
    void element.offsetWidth;
    element.classList.add('app-highlight');
    element.addEventListener(
      'animationend',
      () => element.classList.remove('app-highlight'),
      { once: true },
    );
  };
  requestAnimationFrame(step);
}
