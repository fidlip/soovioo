import { describe, expect, it } from 'vitest';
import { nextMonday } from './week';

describe('nextMonday', () => {
  it.each([
    ['a Monday: a week later', new Date(2026, 10, 2), new Date(2026, 10, 9)],
    ['a Tuesday', new Date(2026, 10, 3), new Date(2026, 10, 9)],
    ['a Sunday: the next day', new Date(2026, 10, 8), new Date(2026, 10, 9)],
    ['across a year', new Date(2026, 11, 30), new Date(2027, 0, 4)],
  ])('%s', (_, from, monday) => {
    expect(nextMonday(from)).toEqual(monday);
  });
});
