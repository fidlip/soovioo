import { FieldErrorDirective } from './field-error.directive';
import { SubmitButton } from './submit-button';

export { ClearFilters } from './clear-filters';
export { FloatField } from './float-field';

/** Import into a page with forms: automatic required markers + field
 * errors on every named ngModel control, and the guarded submit button. */
export const FORM_FIELDS = [FieldErrorDirective, SubmitButton] as const;
