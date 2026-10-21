import { foldForSearch, matchesContact } from './admin-page';

describe('foldForSearch', () => {
  it('drops diacritics and case', () => {
    expect(foldForSearch('Šalda Žluťoučký')).toBe('salda zlutoucky');
  });
});

describe('matchesContact', () => {
  const user = { email: 'Jan.Novak@Example.cz', phone: '+420 777 123 456' };

  it('matches the e-mail case-insensitively', () => {
    expect(matchesContact(user, 'novak@ex')).toBe(true);
  });

  it('matches the phone ignoring spaces and the +420 prefix', () => {
    expect(matchesContact(user, '777123')).toBe(true);
    expect(matchesContact(user, '777 123 456')).toBe(true);
    expect(matchesContact(user, '+420777')).toBe(true);
    expect(matchesContact({ phone: '777123456' }, '00420 777')).toBe(true);
  });

  it('rejects a non-matching query and users without contacts', () => {
    expect(matchesContact(user, '608')).toBe(false);
    expect(matchesContact({ email: null, phone: null }, 'a')).toBe(false);
  });
});
