import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { NotifyService, isServerOrNetworkError } from './notify.service';

/** Network failures and 500/502/504 get one toast here, whatever the page does with
 * the error afterwards — pages only need to handle the 4xx (and meaningful 503s) they expect. */
export const serverErrorInterceptor: HttpInterceptorFn = (req, next) => {
  const notify = inject(NotifyService);
  return next(req).pipe(
    catchError((err: unknown) => {
      if (isServerOrNetworkError(err)) {
        notify.error(
          (err as { status: number }).status === 0
            ? 'Server je nedostupný. Zkontrolujte připojení a zkuste to znovu.'
            : 'Na serveru došlo k chybě. Zkuste to prosím znovu.',
        );
      }
      return throwError(() => err);
    }),
  );
};
