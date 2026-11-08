import { ChangeDetectorRef, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** For shell components showing the session from `AuthApiService`, which
 * isn't signal-based: logging in or out, entering support mode or a demo
 * all end with a navigation, so the component is checked after each one.
 * Call from a constructor. */
export function checkOnNavigation(): void {
  const changeDetector = inject(ChangeDetectorRef);
  inject(Router)
    .events.pipe(
      filter((e) => e instanceof NavigationEnd),
      takeUntilDestroyed(),
    )
    .subscribe(() => changeDetector.markForCheck());
}
