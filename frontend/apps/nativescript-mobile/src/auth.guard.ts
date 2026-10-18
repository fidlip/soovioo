import { inject } from '@angular/core';
import { CanActivateFn, UrlTree } from '@angular/router';
import { Router } from '@angular/router';
import { AuthApiService } from '@soovioo/shared';

export const authGuard: CanActivateFn = (): boolean | UrlTree => {
  const authApi = inject(AuthApiService);
  const router = inject(Router);

  if (authApi.isLoggedIn()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};
