import { formatAmount, formatDate } from './date-format';

describe('formatDate', () => {
  // 2026-10-24 07:05:07 UTC = 9:05:07 in Prague (CEST).
  const moment = '2026-10-24T07:05:07Z';

  it('shows a moment in Prague time in every named format', () => {
    expect(formatDate(moment, 'date')).toBe('24. 10. 2026');
    expect(formatDate(moment, 'dateTime')).toBe('24. 10. 2026 9:05');
    expect(formatDate(moment, 'time')).toBe('9:05');
    expect(formatDate(moment, 'timeWithSeconds')).toBe('9:05:07');
    expect(formatDate(moment, 'dayMonth')).toBe('24. 10.');
    expect(formatDate(moment, 'dayMonthTime')).toBe('24. 10. 9:05');
  });

  it('moves a late-evening UTC moment to the next Prague day', () => {
    expect(formatDate('2026-10-24T22:30:00Z', 'date')).toBe('25. 10. 2026');
  });

  it('keeps a date-only value on its calendar day', () => {
    expect(formatDate('2026-01-01', 'date')).toBe('1. 1. 2026');
  });

  it('accepts a Date', () => {
    expect(formatDate(new Date(moment), 'time')).toBe('9:05');
  });
});

describe('formatAmount', () => {
  it('groups thousands and appends Kč', () => {
    expect(formatAmount(1234.5).replace(/\s/g, ' ')).toBe('1 234,5 Kč');
  });
});
