import { matchServerError } from './field-error.directive';

describe('matchServerError', () => {
  it('matches a field by the suffix of the control name', () => {
    expect(matchServerError({ firstName: ['Chybí.'] }, 'userFirstName')).toBe(
      'Chybí.',
    );
  });

  it('matches a list item only exactly', () => {
    const errors = { 'periods[1].to': ['Pozdě.'] };
    expect(matchServerError(errors, 'periods[1].to')).toBe('Pozdě.');
    expect(matchServerError(errors, 'periods[0].to')).toBeNull();
    expect(matchServerError(errors, 'to')).toBeNull();
    expect(matchServerError({ to: ['Pozdě.'] }, 'periods[1].to')).toBeNull();
  });
});
