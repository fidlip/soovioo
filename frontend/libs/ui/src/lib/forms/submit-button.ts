import {
  Component,
  DestroyRef,
  ElementRef,
  effect,
  inject,
  input,
  signal,
} from '@angular/core';
import { Subscription } from 'rxjs';
import { NgForm } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import { cssEscape, fieldLabelText } from './field-label';

/** Submit button that stays disabled while its form is invalid. Hovering
 * (or focusing) the disabled button lists what's missing in a tooltip and
 * reveals the errors of fields the user hasn't touched yet. `iconOnly` shows
 * just the icon; the label is then its accessible name, and `tooltip` the
 * tooltip of the enabled button. */
@Component({
  selector: 'app-submit-button',
  imports: [Button, Tooltip],
  template: `<span
    class="inline-flex"
    [class.w-full]="fullWidth()"
    [pTooltip]="invalid() ? hint() : tooltip()"
    tooltipPosition="top"
    (mouseenter)="reveal()"
    (focusin)="reveal()"
  >
    <p-button
      type="submit"
      [label]="iconOnly() ? undefined : label()"
      [ariaLabel]="iconOnly() ? label() : undefined"
      [icon]="icon()"
      [severity]="severity()"
      [outlined]="outlined()"
      [disabled]="invalid() || busy() || disabled()"
      [styleClass]="(fullWidth() ? 'w-full ' : '') + styleClass()"
      [class.w-full]="fullWidth()"
    />
  </span>`,
  host: { class: 'inline-flex' },
})
export class SubmitButton {
  readonly form = input.required<NgForm>();
  readonly label = input('Uložit');
  readonly icon = input('pi pi-check');
  readonly severity = input<'primary' | 'secondary' | 'danger' | undefined>(
    undefined,
  );
  readonly styleClass = input('');
  readonly outlined = input(false);
  readonly fullWidth = input(false);
  readonly busy = input(false);
  /** Disabled on top of an invalid form, e.g. while an optional field it
   * needs is empty. */
  readonly disabled = input(false);
  readonly iconOnly = input(false);
  readonly tooltip = input('');

  private readonly host = inject(ElementRef<HTMLElement>)
    .nativeElement as HTMLElement;
  protected readonly hint = signal('');
  /** Mirrors the form's validity as a signal — the form's status changes
   * outside this component's own inputs, so a plain `form().invalid` in the
   * template wouldn't re-render. */
  protected readonly invalid = signal(true);

  constructor() {
    let subscription: Subscription | undefined;
    effect(() => {
      const form = this.form();
      subscription?.unsubscribe();
      subscription = form.statusChanges?.subscribe(() =>
        this.invalid.set(!!form.invalid),
      );
      queueMicrotask(() => this.invalid.set(!!form.invalid));
    });
    inject(DestroyRef).onDestroy(() => subscription?.unsubscribe());
  }

  protected reveal(): void {
    const form = this.form();
    if (!form.invalid) {
      return;
    }
    form.control.markAllAsTouched();
    const formEl = this.host.closest('form');
    const missing = Object.entries(form.controls)
      .filter(([, control]) => control.invalid)
      .map(([name]) => {
        const el = formEl?.querySelector(`[name="${cssEscape(name)}"]`);
        return el ? fieldLabelText(el) : name;
      })
      .filter((label, i, all) => label && all.indexOf(label) === i);
    this.hint.set(
      missing.length > 0
        ? `Vyplňte: ${missing.join(', ')}`
        : 'Formulář není vyplněn správně.',
    );
  }
}
