import { EmailFormat, PhoneFormat } from './contact-format.directive';
import { FieldErrorDirective } from './field-error.directive';
import { SubmitButton } from './submit-button';

export { ClearFilters } from './clear-filters';
export { FloatField } from './float-field';
export { studentClasses, studentPickOptions } from './student-pick';

/** Import into a page with forms: automatic required markers + field
 * errors on every named ngModel control, the guarded submit button, and
 * `appEmail` / `appPhone` format checks. */
export const FORM_FIELDS = [
  FieldErrorDirective,
  SubmitButton,
  EmailFormat,
  PhoneFormat,
] as const;
