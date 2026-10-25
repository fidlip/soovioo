/** The one locale the app formats dates, times and numbers in — switching
 * languages later means changing it here (and the web's LOCALE_ID, which
 * is set from it). */
export const APP_LOCALE = 'cs-CZ';

/** Every school is in Czechia: times show in Prague, not in the viewer's
 * time zone. */
export const SCHOOL_TIME_ZONE = 'Europe/Prague';

/** Named formats, shown here for cs-CZ:
 * - `date` — 24. 10. 2026
 * - `dateTime` — 24. 10. 2026 9:05
 * - `time` — 9:05
 * - `timeWithSeconds` — 9:05:07
 * - `dayMonth` — 24. 10.
 * - `dayMonthTime` — 24. 10. 9:05 */
export type DateFormat =
  | 'date'
  | 'dateTime'
  | 'time'
  | 'timeWithSeconds'
  | 'dayMonth'
  | 'dayMonthTime';

const OPTIONS: Record<DateFormat, Intl.DateTimeFormatOptions> = {
  date: { day: 'numeric', month: 'numeric', year: 'numeric' },
  dateTime: {
    day: 'numeric',
    month: 'numeric',
    year: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  },
  time: { hour: 'numeric', minute: '2-digit' },
  timeWithSeconds: { hour: 'numeric', minute: '2-digit', second: '2-digit' },
  dayMonth: { day: 'numeric', month: 'numeric' },
  dayMonthTime: {
    day: 'numeric',
    month: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  },
};

const DATE_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/;

const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(
  format: DateFormat,
  locale: string,
  timeZone: string,
): Intl.DateTimeFormat {
  const key = `${format}|${locale}|${timeZone}`;
  let f = formatters.get(key);
  if (!f) {
    f = new Intl.DateTimeFormat(locale, { ...OPTIONS[format], timeZone });
    formatters.set(key, f);
  }
  return f;
}

/** Formats a moment (ISO string or Date) in the school's time zone. A
 * date-only "2026-10-24" (DateOnly on the backend) is a calendar day, not
 * a moment — it's shown as is, never shifted by a time zone. */
export function formatDate(
  value: string | Date,
  format: DateFormat,
  locale: string = APP_LOCALE,
): string {
  const dateOnly = typeof value === 'string' ? DATE_ONLY.exec(value) : null;
  if (dateOnly) {
    const [, year, month, day] = dateOnly.map(Number);
    return formatter(format, locale, 'UTC').format(
      new Date(Date.UTC(year, month - 1, day)),
    );
  }
  return formatter(format, locale, SCHOOL_TIME_ZONE).format(new Date(value));
}

/** "1 234,5 Kč". */
export function formatAmount(
  amount: number,
  locale: string = APP_LOCALE,
): string {
  return `${amount.toLocaleString(locale, { maximumFractionDigits: 2 })} Kč`;
}
