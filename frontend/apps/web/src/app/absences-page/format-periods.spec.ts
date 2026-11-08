import { formatPeriods } from './excuse-days';

describe('formatPeriods', () => {
  it('collapses consecutive periods into ranges', () => {
    expect(formatPeriods([1, 2, 3, 5])).toBe('1.–3., 5.');
    expect(formatPeriods([0])).toBe('0.');
    expect(formatPeriods([2, 4, 6])).toBe('2., 4., 6.');
  });

  it('treats no periods as the whole day', () => {
    expect(formatPeriods([])).toBe('celý den');
  });
});
