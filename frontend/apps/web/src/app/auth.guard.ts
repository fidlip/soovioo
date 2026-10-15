import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, Router } from '@angular/router';
import { AuthApiService } from '@sova/shared';
import { isPathBasedTenant } from './school-context';

// Reads `schoolSlug` from the guard's own candidate route rather than from
// SchoolContextService — the service only updates on NavigationEnd, which
// hasn't fired yet for the navigation this guard is deciding.
export const authGuard: CanActivateFn = (route: ActivatedRouteSnapshot) => {
  const authApi = inject(AuthApiService);
  const router = inject(Router);

  if (authApi.isLoggedIn()) {
    return true;
  }

  if (isPathBasedTenant()) {
    // No `schoolSlug` param means this route isn't nested under `:schoolSlug`
    // at all — that's the reserved SuperAdmin area (`/superadmin`).
    const slug = route.paramMap.get('schoolSlug');
    return router.createUrlTree(slug ? ['/', slug, 'login'] : ['/', 'superadmin', 'login']);
  }
  return router.createUrlTree(['/', 'login']);
};
