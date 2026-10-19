import {
  DestroyRef,
  Directive,
  ElementRef,
  Renderer2,
  afterNextRender,
  effect,
  inject,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NgControl, ValidationErrors } from '@angular/forms';
import { NotifyService } from '../notify.service';
import { labelElementFor } from './field-label';

/** Applied automatically to every named `ngModel` control in a component
 * that imports it (see FORM_FIELDS):
 * - a control with `required` gets an asterisk on its label (or in its
 *   placeholder when it has no label),
 * - once the user leaves an invalid field, a short error shows under it,
 * - field errors from the server (ValidationProblemDetails, routed through
 *   NotifyService) land on the control whose `name` matches the key, until
 *   the value changes. */
// Deliberately matches every named ngModel (no `app` prefix) — that's what
// makes it automatic.
// eslint-disable-next-line @angular-eslint/directive-selector
@Directive({ selector: '[ngModel][name]' })
export class FieldErrorDirective {
  private readonly control = inject(NgControl);
  private readonly host = inject(ElementRef<HTMLElement>).nativeElement as HTMLElement;
  private readonly renderer = inject(Renderer2);
  private readonly destroyRef = inject(DestroyRef);
  private readonly notify = inject(NotifyService);
  private messageEl: HTMLElement | null = null;
  private readonly createdAt = Date.now();

  constructor() {
    afterNextRender(() => this.markRequired());

    effect(() => {
      const fieldErrors = this.notify.serverFieldErrors();
      const message =
        fieldErrors && fieldErrors.at >= this.createdAt
          ? matchServerError(fieldErrors.errors, this.control.name)
          : null;
      const control = this.control.control;
      if (message && control) {
        control.setErrors({ ...(control.errors ?? {}), server: message });
        control.markAsTouched();
        this.render();
      }
    });

    this.control.control?.events
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.render());
    this.control.valueChanges
      ?.pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => {
        const control = this.control.control;
        if (control?.hasError('server')) {
          const rest = { ...control.errors };
          delete rest['server'];
          control.setErrors(Object.keys(rest).length > 0 ? rest : null);
        }
      });
    this.destroyRef.onDestroy(() => this.messageEl?.remove());
  }

  private markRequired(): void {
    if (!this.host.hasAttribute('required')) {
      return;
    }
    const label = labelElementFor(this.host);
    if (label) {
      label.classList.add('field-required');
      return;
    }
    const input =
      this.host instanceof HTMLInputElement || this.host instanceof HTMLTextAreaElement
        ? this.host
        : this.host.querySelector<HTMLInputElement>('input[placeholder], textarea[placeholder]');
    if (input?.placeholder && !input.placeholder.endsWith('*')) {
      input.placeholder = `${input.placeholder} *`;
    }
  }

  private render(): void {
    const control = this.control.control;
    const message =
      control && control.invalid && control.touched
        ? messageFor(control.errors)
        : null;
    if (!message) {
      this.messageEl?.remove();
      this.messageEl = null;
      return;
    }
    let el = this.messageEl;
    if (!el) {
      el = this.renderer.createElement('small') as HTMLElement;
      this.renderer.addClass(el, 'field-error');
      this.renderer.setAttribute(el, 'role', 'alert');
      this.messageEl = el;
    }
    el.textContent = message;
    if (el.previousElementSibling !== this.host) {
      this.host.insertAdjacentElement('afterend', el);
    }
  }
}

function messageFor(errors: ValidationErrors | null): string | null {
  if (!errors) {
    return null;
  }
  if (errors['server']) {
    return errors['server'] as string;
  }
  if (errors['required']) {
    return 'Povinné pole';
  }
  if (errors['email']) {
    return 'Neplatný e-mail';
  }
  if (errors['minlength']) {
    return `Alespoň ${errors['minlength'].requiredLength} znaků`;
  }
  if (errors['maxlength']) {
    return `Nejvýše ${errors['maxlength'].requiredLength} znaků`;
  }
  if (errors['min']) {
    return `Nejméně ${errors['min'].min}`;
  }
  if (errors['max']) {
    return `Nejvýše ${errors['max'].max}`;
  }
  if (errors['pattern']) {
    return 'Neplatný formát';
  }
  return 'Neplatná hodnota';
}

/** Server keys are camelCase DTO property names (`firstName`, maybe
 * prefixed like `request.firstName`); control names are often prefixed
 * by the form (`userFirstName`, `newText`) — match on the suffix. */
export function matchServerError(
  errors: Record<string, string[]> | null,
  controlName: string | number | null,
): string | null {
  if (!errors || controlName === null) {
    return null;
  }
  const name = String(controlName).toLowerCase();
  for (const [key, messages] of Object.entries(errors)) {
    const field = key.split('.').pop()?.toLowerCase();
    if (field && messages.length > 0 && name.endsWith(field)) {
      return messages.join(' ');
    }
  }
  return null;
}
