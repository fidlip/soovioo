import { WeekParity } from './timetable-entry.model';

/** ISO 8601 week number (weeks start on Monday, week 1 contains Jan 4). */
export function isoWeekNumber(date: Date): number {
  const d = new Date(
    Date.UTC(date.getFullYear(), date.getMonth(), date.getDate()),
  );
  const dayNum = d.getUTCDay() || 7;
  d.setUTCDate(d.getUTCDate() + 4 - dayNum);
  const yearStart = new Date(Date.UTC(d.getUTCFullYear(), 0, 1));
  return Math.ceil(((d.getTime() - yearStart.getTime()) / 86400000 + 1) / 7);
}

/** Odd/even week (by ISO week number) — which "lichý/sudý" timetable
 * entries apply on that date. */
export function weekParityOf(date: Date): Exclude<WeekParity, 'Every'> {
  return isoWeekNumber(date) % 2 === 1 ? 'Odd' : 'Even';
}

/** The Monday of the week after `date`'s. */
export function nextMonday(date: Date): Date {
  const daysAhead = (8 - date.getDay()) % 7 || 7;
  return new Date(
    date.getFullYear(),
    date.getMonth(),
    date.getDate() + daysAhead,
  );
}
