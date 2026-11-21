import { EmailFormat, PhoneFormat } from './contact-format.directive';
import { FieldErrorDirective } from './field-error.directive';
import { SubmitButton } from './submit-button';

export { ClearFilters } from './clear-filters';
export { EmailFormat, PhoneFormat } from './contact-format.directive';
export { FieldErrorDirective, matchServerError } from './field-error.directive';
export { cssEscape, fieldLabelText, labelElementFor } from './field-label';
export { FloatField } from './float-field';
export { SubmitButton } from './submit-button';

/** Import into a page with forms: automatic required markers + field
 * errors on every named ngModel control, the guarded submit button, and
 * `appEmail` / `appPhone` format checks. */
export const FORM_FIELDS = [
  FieldErrorDirective,
  SubmitButton,
  EmailFormat,
  PhoneFormat,
] as const;
