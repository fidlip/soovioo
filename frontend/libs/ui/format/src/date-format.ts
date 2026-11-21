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

/** The mobile app's JavaScript engine (NativeScript's V8) is built
 * without Intl — there the formats below are put together by hand, for
 * cs-CZ and Prague only. */
const HAS_INTL =
  typeof Intl !== 'undefined' && typeof Intl.DateTimeFormat === 'function';

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
  if (!HAS_INTL) {
    return formatWithoutIntl(value, format);
  }
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
  if (!HAS_INTL) {
    return `${formatNumberWithoutIntl(amount)} Kč`;
  }
  return `${amount.toLocaleString(locale, { maximumFractionDigits: 2 })} Kč`;
}

/** {@link formatDate} for cs-CZ in Prague without Intl — exported for the
 * tests that hold it to Intl's output. */
export function formatWithoutIntl(
  value: string | Date,
  format: DateFormat,
): string {
  const dateOnly = typeof value === 'string' ? DATE_ONLY.exec(value) : null;
  let d: Date;
  if (dateOnly) {
    const [, year, month, day] = dateOnly.map(Number);
    d = new Date(Date.UTC(year, month - 1, day));
  } else {
    const utc = new Date(value).getTime();
    d = new Date(utc + pragueOffsetMinutes(utc) * 60_000);
  }
  // `d` holds the wall-clock time in its UTC fields.
  const day = `${d.getUTCDate()}. ${d.getUTCMonth() + 1}.`;
  const date = `${day} ${d.getUTCFullYear()}`;
  const two = (n: number) => `${n}`.padStart(2, '0');
  const time = `${d.getUTCHours()}:${two(d.getUTCMinutes())}`;
  switch (format) {
    case 'date':
      return date;
    case 'dateTime':
      return `${date} ${time}`;
    case 'time':
      return time;
    case 'timeWithSeconds':
      return `${time}:${two(d.getUTCSeconds())}`;
    case 'dayMonth':
      return day;
    case 'dayMonthTime':
      return `${day} ${time}`;
  }
}

/** Prague's offset from UTC: summer time (+2 h) from 1:00 UTC on the last
 * Sunday of March to 1:00 UTC on the last Sunday of October, otherwise +1 h. */
function pragueOffsetMinutes(utc: number): number {
  const year = new Date(utc).getUTCFullYear();
  const lastSundayAt1 = (month: number) => {
    const last = new Date(Date.UTC(year, month + 1, 0, 1));
    last.setUTCDate(last.getUTCDate() - last.getUTCDay());
    return last.getTime();
  };
  return utc >= lastSundayAt1(2) && utc < lastSundayAt1(9) ? 120 : 60;
}

/** "1 234,5" — non-breaking space between thousands, at most 2 decimals. */
export function formatNumberWithoutIntl(value: number): string {
  const [whole, fraction] = (Math.round(value * 100) / 100)
    .toString()
    .split('.');
  const sign = whole.startsWith('-') ? '-' : '';
  const digits = whole.replace('-', '');
  const grouped = digits.replace(/\B(?=(\d{3})+(?!\d))/g, '\u00a0');
  return `${sign}${grouped}${fraction ? `,${fraction}` : ''}`;
}
