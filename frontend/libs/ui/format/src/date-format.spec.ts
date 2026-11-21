import {
  DateFormat,
  formatAmount,
  formatDate,
  formatNumberWithoutIntl,
  formatWithoutIntl,
} from './date-format';

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

describe('without Intl (the mobile app)', () => {
  const formats: DateFormat[] = [
    'date',
    'dateTime',
    'time',
    'timeWithSeconds',
    'dayMonth',
    'dayMonthTime',
  ];

  it('gives what Intl gives, across summer time changes and midnights', () => {
    const moments = [
      '2026-03-29T00:59:59Z', // the last winter second
      '2026-03-29T01:00:00Z', // summer time from here
      '2026-10-25T00:59:00Z',
      '2026-10-25T01:00:00Z', // back to winter time
      '2026-10-24T22:05:07Z', // after midnight in Prague
      '2027-01-01T08:05:00Z',
      '2028-03-26T01:30:00Z',
    ];
    for (const moment of moments) {
      for (const format of formats) {
        expect(formatWithoutIntl(moment, format)).toBe(
          formatDate(moment, format),
        );
      }
    }
  });

  it('keeps a date-only value on its day', () => {
    expect(formatWithoutIntl('2026-01-01', 'date')).toBe('1. 1. 2026');
  });

  it('groups thousands and uses a decimal comma', () => {
    expect(formatNumberWithoutIntl(1234.5)).toBe('1\u00a0234,5');
    expect(formatNumberWithoutIntl(-1234567)).toBe('-1\u00a0234\u00a0567');
    expect(formatNumberWithoutIntl(0.125)).toBe('0,13');
  });
});
