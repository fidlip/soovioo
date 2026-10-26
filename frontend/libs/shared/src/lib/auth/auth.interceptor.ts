import {
  HttpErrorResponse,
  HttpInterceptorFn,
  HttpRequest,
} from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { AuthApiService } from './auth-api.service';
import { TokenStorage } from './token-storage';

const withBearer = (req: HttpRequest<unknown>, token: string | null) =>
  token ? req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }) : req;

/**
 * Adds the Bearer access token and, when the API answers 401 (the 15-minute
 * token expired), refreshes the session once and retries. If that refresh
 * fails too, the session is over: `AuthApiService.sessionExpired$` fires and
 * the original 401 propagates. A 403 may mean the role's permissions
 * changed — the session is refreshed in the background so the token (and
 * what the UI offers) catches up.
 */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const tokenStorage = inject(TokenStorage);
  const baseUrl = inject(API_BASE_URL);
  const authApi = inject(AuthApiService);

  const token = tokenStorage.getToken();
  const isAuthEndpoint = req.url.startsWith(`${baseUrl}/api/auth/`);
  if (isAuthEndpoint) {
    return next(req);
  }

  return next(withBearer(req, token)).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 403 &&
        token &&
        !authApi.getSupportInfo()
      ) {
        authApi.refresh().subscribe();
      }
      // Only retry calls that were made with a (now stale) session; an
      // anonymous 401 has nothing to refresh.
      if (
        !(error instanceof HttpErrorResponse) ||
        error.status !== 401 ||
        !token
      ) {
        return throwError(() => error);
      }
      return authApi.refresh().pipe(
        switchMap((fresh) => {
          if (!fresh) {
            authApi.notifySessionExpired();
            return throwError(() => error);
          }
          return next(withBearer(req, fresh));
        }),
      );
    }),
  );
};
