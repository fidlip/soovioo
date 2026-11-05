import { inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivateChildFn,
  RedirectCommand,
  Router,
} from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { isPathBasedTenant, subdomainSlug } from './school-context';
import { SchoolLookupService } from './school-lookup.service';

/** Every subdomain has a valid (wildcard) certificate, so a mistyped or
 * deleted school would otherwise land on a login form nobody can use —
 * show "škola neexistuje" instead — under the typed address, so a typo
 * can be fixed in place. Fails open when the API can't be
 * reached: the pages then report the error themselves. */
export const schoolExistsGuard: CanActivateChildFn = (
  route: ActivatedRouteSnapshot,
) => {
  const router = inject(Router);
  const slug = isPathBasedTenant()
    ? route.paramMap.get('schoolSlug')
    : subdomainSlug(window.location.hostname);
  if (!slug) {
    // Bare main domain — no school to check.
    return true;
  }
  return inject(SchoolLookupService)
    .lookup(slug)
    .pipe(
      map((school) =>
        school
          ? true
          : new RedirectCommand(
              router.createUrlTree(['/', 'school-not-found'], {
                queryParams: { slug },
              }),
              { skipLocationChange: true },
            ),
      ),
      catchError(() => of(true)),
    );
};
