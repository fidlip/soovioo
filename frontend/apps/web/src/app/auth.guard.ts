import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthApiService } from '@sova/shared';

export const authGuard: CanActivateFn = () => {
  const authApi = inject(AuthApiService);
  const router = inject(Router);

  if (authApi.isLoggedIn()) {
    return true;
  }
  return router.createUrlTree(['/login']);
};
