import { Directive } from '@angular/core';
import {
  AbstractControl,
  NG_VALIDATORS,
  ValidationErrors,
  Validator,
} from '@angular/forms';
import { parseEmail, parsePhone } from '@soovioo/shared';

/** An e-mail by the server's rules (an empty value passes). */
@Directive({
  selector: 'input[appEmail]',
  providers: [
    { provide: NG_VALIDATORS, useExisting: EmailFormat, multi: true },
  ],
})
export class EmailFormat implements Validator {
  validate(control: AbstractControl): ValidationErrors | null {
    return parseEmail(control.value).valid ? null : { email: true };
  }
}

/** A phone number by the server's rules (an empty value passes). */
@Directive({
  selector: 'input[appPhone]',
  providers: [
    { provide: NG_VALIDATORS, useExisting: PhoneFormat, multi: true },
  ],
})
export class PhoneFormat implements Validator {
  validate(control: AbstractControl): ValidationErrors | null {
    return parsePhone(control.value).valid ? null : { phone: true };
  }
}
