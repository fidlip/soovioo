import { inject } from '@angular/core';
import {
  ActivatedRouteSnapshot,
  CanActivateFn,
  Router,
  RouterStateSnapshot,
} from '@angular/router';
import { AuthApiService } from '@soovioo/shared';
import {
  isPathBasedTenant,
  subdomainSlug,
  superAdminUrl,
} from './school-context';

// Reads `schoolSlug` from the guard's own candidate route rather than from
// SchoolContextService — the service only updates on NavigationEnd, which
// hasn't fired yet for the navigation this guard is deciding.
//
// A school's page (other than Home) goes to the login with `?returnUrl=` (e.g. a link from the
// daily e-mail), which the login page returns to.
export const authGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
  state: RouterStateSnapshot,
) => {
  const authApi = inject(AuthApiService);
  const router = inject(Router);

  if (!authApi.isLoggedIn()) {
    // No `schoolSlug` param means this route isn't nested under `:schoolSlug`
    // at all — that's the reserved SuperAdmin area (`/superadmin`).
    const slug = route.paramMap.get('schoolSlug');
    const home = router.serializeUrl(
      router.createUrlTree(slug ? ['/', slug, 'home'] : ['/', 'home']),
    );
    const returnUrl =
      state.url === home ? {} : { queryParams: { returnUrl: state.url } };
    if (isPathBasedTenant()) {
      return slug
        ? router.createUrlTree(['/', slug, 'login'], returnUrl)
        : router.createUrlTree(['/', 'superadmin', 'login']);
    }
    return router.createUrlTree(['/', 'login'], returnUrl);
  }

  // An Admin assigned this account's current password (new account or
  // reset) and its owner hasn't picked their own yet — every destination
  // except Settings itself (where the change-password form lives) bounces
  // there first, or the forced change could be dodged by just not visiting
  // it. `route.routeConfig.path` is 'settings' in the normal :schoolSlug
  // tree and 'superadmin/settings' in the SuperAdmin tree — both end in it.
  const isSettingsRoute =
    route.routeConfig?.path?.endsWith('settings') ?? false;
  if (authApi.mustChangePassword() && !isSettingsRoute) {
    if (isPathBasedTenant()) {
      const slug = route.paramMap.get('schoolSlug');
      return router.createUrlTree(
        slug ? ['/', slug, 'settings'] : ['/', 'superadmin', 'settings'],
      );
    }
    return router.createUrlTree(
      authApi.isSuperAdmin()
        ? ['/', 'superadmin', 'settings']
        : ['/', 'settings'],
    );
  }

  // A plain SuperAdmin session on a school's page (support mode ended or
  // expired, or just a typed URL): the SuperAdmin works in schools only via
  // support mode — back to the SuperAdmin area (the API refuses them on
  // school endpoints anyway, see SuperAdminScopeFilter).
  const onSchoolPage = isPathBasedTenant()
    ? !!route.paramMap.get('schoolSlug')
    : !!subdomainSlug(window.location.hostname) &&
      !(route.routeConfig?.path ?? '').startsWith('superadmin');
  if (onSchoolPage && authApi.isSuperAdmin()) {
    if (isPathBasedTenant()) {
      return router.createUrlTree(['/', 'superadmin']);
    }
    window.location.href = superAdminUrl();
    return false;
  }

  return true;
};
