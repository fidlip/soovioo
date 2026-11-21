import { OPTIMUS_TRANSLATION } from './locale';

describe('OPTIMUS_TRANSLATION', () => {
  it('names days from Sunday and months from January in Czech', () => {
    expect(OPTIMUS_TRANSLATION.dayNames).toEqual([
      'neděle',
      'pondělí',
      'úterý',
      'středa',
      'čtvrtek',
      'pátek',
      'sobota',
    ]);
    expect(OPTIMUS_TRANSLATION.dayNamesMin?.[1]).toBe('po');
    expect(OPTIMUS_TRANSLATION.monthNames?.[0]).toBe('leden');
    expect(OPTIMUS_TRANSLATION.monthNamesShort?.[6]).toBe('čvc');
  });

  it('starts the week on Monday', () => {
    expect(OPTIMUS_TRANSLATION.firstDayOfWeek).toBe(1);
  });
});
