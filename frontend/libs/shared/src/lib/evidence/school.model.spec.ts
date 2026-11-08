import { formatBankAccount } from './school.model';

describe('formatBankAccount', () => {
  it('shows a Czech IBAN with a prefix in the domestic form', () => {
    expect(formatBankAccount('CZ6508000000192000145399')).toBe(
      '19-2000145399/0800',
    );
  });

  it('leaves out a zero prefix and leading zeros', () => {
    expect(formatBankAccount('CZ5508000000000123456789')).toBe(
      '123456789/0800',
    );
  });

  it('keeps a foreign IBAN as an IBAN', () => {
    expect(formatBankAccount('SK3112000000198742637541')).toBe(
      'SK31 1200 0000 1987 4263 7541',
    );
  });
});
