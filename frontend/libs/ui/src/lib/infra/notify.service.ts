import { HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { MessageService } from '@openng/optimus-ui/api';

const SUCCESS_LIFE_MS = 5_000;
/** A bare 403 — typically a permission the admin took away while the page
 * was open, so it still offered the action. */
export const FORBIDDEN_MESSAGE =
  'Na tuto akci nemáte oprávnění. Pokud ho potřebujete, obraťte se na správce školy.';
const ERROR_LIFE_MS = 30_000;

/** App-wide toasts (`<p-toast>` in app.html) for the outcome of an action —
 * instead of banners that stay on the page until reload. Success fades
 * quickly, errors/warnings stay ~30 s; every toast has a close button. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly messages = inject(MessageService);

  /** Per-field errors of the last rejected request (ValidationProblemDetails
   * `errors` keyed by camelCase field) — picked up by FieldErrorDirective on
   * the matching controls. `at` lets a form opened later ignore them. */
  readonly serverFieldErrors = signal<{
    errors: Record<string, string[]>;
    at: number;
  } | null>(null);

  success(text: string): void {
    this.messages.add({
      severity: 'success',
      summary: text,
      life: SUCCESS_LIFE_MS,
      closable: true,
    });
  }

  warn(text: string): void {
    this.messages.add({
      severity: 'warn',
      summary: text,
      life: ERROR_LIFE_MS,
      closable: true,
    });
  }

  error(text: string): void {
    this.messages.add({
      severity: 'error',
      summary: text,
      life: ERROR_LIFE_MS,
      closable: true,
    });
  }

  /** A failed save/delete/…: logs it and shows the server's own message when
   * it sent one, a generic one otherwise. Network/5xx errors are already
   * toasted by `serverErrorInterceptor`, so they're only logged here. */
  actionFailed(context: string, err: unknown): void {
    console.error(context, err);
    if (isServerOrNetworkError(err)) {
      return;
    }
    const fieldErrors = (err as { error?: { errors?: unknown } })?.error
      ?.errors;
    if (
      fieldErrors &&
      typeof fieldErrors === 'object' &&
      !Array.isArray(fieldErrors)
    ) {
      this.serverFieldErrors.set({
        errors: fieldErrors as Record<string, string[]>,
        at: Date.now(),
      });
    }
    this.error(
      errorMessageOf(err) ??
        (err instanceof HttpErrorResponse && err.status === 403
          ? FORBIDDEN_MESSAGE
          : 'Akci se nepodařilo provést.'),
    );
  }

  /** Drops every toast still on screen — e.g. when a new edit starts, so a
   * stale error about the previous one doesn't linger. */
  clear(): void {
    this.messages.clear();
    this.serverFieldErrors.set(null);
  }
}

export function isServerOrNetworkError(err: unknown): boolean {
  return (
    err instanceof HttpErrorResponse && [0, 500, 502, 504].includes(err.status)
  );
}

/** The API answers a rejected request either with a plain string
 * (`BadRequest("…")`) or with ProblemDetails/ValidationProblemDetails. */
export function errorMessageOf(err: unknown): string | null {
  const body = (err as { error?: unknown })?.error;
  if (typeof body === 'string' && body.trim()) {
    return body;
  }
  if (body && typeof body === 'object') {
    const problem = body as {
      errors?: string[] | Record<string, string[]>;
      detail?: string;
      title?: string;
    };
    if (problem.errors) {
      const list = Array.isArray(problem.errors)
        ? problem.errors
        : Object.values(problem.errors).flat();
      if (list.length > 0) {
        return list.join(' ');
      }
    }
    return problem.detail ?? problem.title ?? null;
  }
  return null;
}
