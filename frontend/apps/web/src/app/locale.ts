import { LocaleInput } from '@fullcalendar/core';
import csLocale from '@fullcalendar/core/locales/cs';
import { Translation } from '@openng/optimus-ui/api';
import { APP_LOCALE } from '@soovioo/shared';

/** The one place the web's locale-dependent widgets take their language
 * from — a different APP_LOCALE needs a matching FullCalendar locale here
 * and Czech texts below translated; names of days and months follow on
 * their own. */

/** What a date picker shows and parses ("24.10.2026"). Kept without the
 * spaces of the displayed dates ("24. 10. 2026"): the picker parses typed
 * text strictly by this pattern, and people type dates without them. */
export const DATE_INPUT_FORMAT = 'dd.mm.yy';

export const CALENDAR_LOCALE: LocaleInput = csLocale;

function names(
  count: number,
  date: (i: number) => Date,
  style: Intl.DateTimeFormatOptions,
): string[] {
  const format = new Intl.DateTimeFormat(APP_LOCALE, {
    ...style,
    timeZone: 'UTC',
  });
  return Array.from({ length: count }, (_, i) => format.format(date(i)));
}

// 2023-01-01 was a Sunday — the pickers count days from Sunday.
const day = (i: number) => new Date(Date.UTC(2023, 0, 1 + i));
const month = (i: number) => new Date(Date.UTC(2023, i, 1));

/** Monday = 1, as optimus-ui counts (Sunday = 0). */
function firstDayOfWeek(): number {
  const locale = new Intl.Locale(APP_LOCALE) as Intl.Locale & {
    getWeekInfo?: () => { firstDay: number };
    weekInfo?: { firstDay: number };
  };
  const info = locale.getWeekInfo?.() ?? locale.weekInfo;
  return (info?.firstDay ?? 1) % 7;
}

/** optimus-ui (PrimeNG) components don't follow Angular's LOCALE_ID —
 * they get their day and month names from here. */
export const OPTIMUS_TRANSLATION: Translation = {
  dayNames: names(7, day, { weekday: 'long' }),
  dayNamesShort: names(7, day, { weekday: 'short' }),
  dayNamesMin: names(7, day, { weekday: 'short' }),
  monthNames: names(12, month, { month: 'long' }),
  monthNamesShort: names(12, month, { month: 'short' }),
  firstDayOfWeek: firstDayOfWeek(),
  dateFormat: DATE_INPUT_FORMAT,
  today: 'Dnes',
  clear: 'Vymazat',
  weekHeader: 'Týd',
  selectionMessage: '{0} vybráno',
  emptySelectionMessage: 'Nic nevybráno',
  emptyMessage: 'Nic nenalezeno',
  emptyFilterMessage: 'Nic nenalezeno',
  emptySearchMessage: 'Nic nenalezeno',
};
