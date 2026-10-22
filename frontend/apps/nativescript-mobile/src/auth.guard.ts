import { inject } from '@angular/core';
import { ActivatedRouteSnapshot, CanActivateFn, UrlTree } from '@angular/router';
import { Router } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';

export const authGuard: CanActivateFn = (
  route: ActivatedRouteSnapshot,
): boolean | UrlTree => {
  const authApi = inject(AuthApiService);
  const router = inject(Router);

  if (!authApi.isLoggedIn()) {
    return router.createUrlTree(['/login']);
  }

  // An Admin assigned this account's current password (new account or
  // reset) and its owner hasn't picked their own yet — every page except
  // Settings (where the change-password form lives) bounces there first,
  // same as the web `auth.guard.ts`.
  if (authApi.mustChangePassword() && route.routeConfig?.path !== 'settings') {
    return router.createUrlTree(['/settings']);
  }
  return true;
};
