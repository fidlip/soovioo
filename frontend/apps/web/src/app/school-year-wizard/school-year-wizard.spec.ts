import { weekdays } from './school-year-wizard';

describe('weekdays', () => {
  it('counts the days from Monday to Friday', () => {
    expect(weekdays(new Date(2026, 10, 16), new Date(2026, 10, 16))).toBe(1);
    expect(weekdays(new Date(2026, 10, 13), new Date(2026, 10, 16))).toBe(2);
    expect(weekdays(new Date(2026, 10, 14), new Date(2026, 10, 15))).toBe(0);
  });
});
