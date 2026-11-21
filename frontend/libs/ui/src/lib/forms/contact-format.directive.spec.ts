import { FormControl } from '@angular/forms';
import { EmailFormat, PhoneFormat } from './contact-format.directive';

describe('EmailFormat', () => {
  const validator = new EmailFormat();

  it('lets an empty value and an e-mail through', () => {
    expect(validator.validate(new FormControl(''))).toBeNull();
    expect(validator.validate(new FormControl('jan@skola.cz'))).toBeNull();
  });

  it('marks what is not an e-mail', () => {
    expect(validator.validate(new FormControl('jan@skola'))).toEqual({
      email: true,
    });
  });
});

describe('PhoneFormat', () => {
  const validator = new PhoneFormat();

  it('lets an empty value and Czech or foreign numbers through', () => {
    expect(validator.validate(new FormControl(''))).toBeNull();
    expect(validator.validate(new FormControl('777 123 456'))).toBeNull();
    expect(validator.validate(new FormControl('+421 905 123 456'))).toBeNull();
  });

  it('marks what is not a phone number', () => {
    expect(validator.validate(new FormControl('12345'))).toEqual({
      phone: true,
    });
  });
});
