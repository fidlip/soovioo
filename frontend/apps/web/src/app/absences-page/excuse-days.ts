import { Excuse, formatDate } from '@soovioo/shared';
import { toDateOnlyString } from './student-day-periods';

/** [1,2,3,5] -> "1.–3., 5."; [] -> "celý den". */
export function formatPeriods(periods: number[]): string {
  if (periods.length === 0) {
    return 'celý den';
  }
  const ranges: string[] = [];
  let start = periods[0];
  let prev = periods[0];
  for (const p of [...periods.slice(1), Number.NaN]) {
    if (p === prev + 1) {
      prev = p;
      continue;
    }
    ranges.push(start === prev ? `${start}.` : `${start}.–${prev}.`);
    start = p;
    prev = p;
  }
  return ranges.join(', ');
}

/** "9. 11. 2026" or "9. 11. 2026 – 11. 11. 2026". */
export function excuseRange(
  excuse: Pick<Excuse, 'dateFrom' | 'dateTo'>,
): string {
  const from = formatDate(excuse.dateFrom, 'date');
  return excuse.dateTo === excuse.dateFrom
    ? from
    : `${from} – ${formatDate(excuse.dateTo, 'date')}`;
}

/** Today as y-m-d, local. */
export function todayString(): string {
  return toDateOnlyString(new Date());
}
