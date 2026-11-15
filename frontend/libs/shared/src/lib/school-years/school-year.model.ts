export type PeriodReport = 'None' | 'Interim' | 'ReportCard';

/** A part of a school year ("období": pololetí, trimestr, čtvrtletí). */
export interface GradingPeriod {
  id: number;
  name: string;
  /** With `to`: the days it covers; the year's last one ends with the year. */
  from: string;
  to: string;
  report: PeriodReport;
  /** The day its report card or assessment is handed out. */
  issuedOn: string | null;
  /** Which of the year's report cards it ends with, from 1. */
  reportNumber: number | null;
  /** Its report card is the year's vysvědčení, not a výpis. */
  final: boolean;
}

export type DayOffKind =
  'Holidays' | 'PublicHoliday' | 'DirectorsDay' | 'Other';

/** Days of a year without lessons. */
export interface SchoolDayOff {
  id: number;
  from: string;
  to: string;
  /** "Podzimní prázdniny" */
  name: string;
  kind: DayOffKind;
  /** Empty = the whole school. */
  classIds: number[];
}

/** A day off as the management sets it; `id` null for a new one. */
export interface SchoolDayOffRequest {
  id: number | null;
  from: string;
  to: string;
  name: string;
  kind: DayOffKind;
  /** Empty = the whole school. */
  classIds: number[];
}

/** Days without lessons, the hlavní prázdniny included. */
export interface DayOffSpan {
  from: string;
  to: string;
  name: string;
  kind: DayOffKind;
  /** Empty = the whole school. */
  classIds: number[];
}

export interface SchoolYear {
  id: number;
  /** "2026/27" */
  name: string;
  from: string;
  to: string;
  teachingStart: string;
  teachingEnd: string;
  /** Today is in it. */
  current: boolean;
  daysOff: SchoolDayOff[];
  periods: GradingPeriod[];
}

/** A period as the management sets it; `id` null for a new one. */
export interface GradingPeriodRequest {
  id: number | null;
  name: string;
  /** The last day; the year's last period ends with the year. */
  to: string;
  report: PeriodReport;
  issuedOn: string | null;
}

/** A year's days and its periods, in order. */
export interface SchoolYearRequest {
  from: string;
  to: string;
  teachingStart: string;
  teachingEnd: string;
  periods: GradingPeriodRequest[];
  /** All the year's days off; left out, they stay. */
  daysOff?: SchoolDayOffRequest[];
  /** Sets the school's okres; left out, it stays. */
  district?: string | null;
}

/** The year to found next, prefilled from MŠMT's calendar. */
export interface SchoolYearSuggestion extends SchoolYearRequest {
  /** The dates are MŠMT's; otherwise an estimate. */
  official: boolean;
  daysOff: SchoolDayOffRequest[];
  /** The okres of the jarní prázdniny in `daysOff`, by default the
   * school's; null without them. */
  district: string | null;
}

export const SUMMER_HOLIDAYS = 'Hlavní prázdniny';

/** The years' days off and the days outside their lessons. */
export function dayOffSpans(years: SchoolYear[]): DayOffSpan[] {
  const dayBefore = (date: string) => shiftDay(date, -1);
  const dayAfter = (date: string) => shiftDay(date, 1);
  return years
    .flatMap((y) => [
      ...y.daysOff.map(({ from, to, name, kind, classIds }) => ({
        from,
        to,
        name,
        kind,
        classIds,
      })),
      {
        from: y.from,
        to: dayBefore(y.teachingStart),
        name: SUMMER_HOLIDAYS,
        kind: 'Holidays' as const,
        classIds: [],
      },
      {
        from: dayAfter(y.teachingEnd),
        to: y.to,
        name: SUMMER_HOLIDAYS,
        kind: 'Holidays' as const,
        classIds: [],
      },
    ])
    .filter((s) => s.from <= s.to)
    .sort((a, b) => a.from.localeCompare(b.from));
}

/** The span that makes `date` (YYYY-MM-DD) a day off for the class
 * (without one: for the whole school); null on a day of lessons. Weekends
 * aside. */
export function dayOffOn(
  spans: DayOffSpan[],
  date: string,
  classId?: number | null,
): DayOffSpan | null {
  return (
    spans.find(
      (s) =>
        s.from <= date &&
        date <= s.to &&
        (s.classIds.length === 0 ||
          (classId != null && s.classIds.includes(classId))),
    ) ?? null
  );
}

function shiftDay(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number);
  const shifted = new Date(Date.UTC(year, month - 1, day + days));
  return shifted.toISOString().slice(0, 10);
}

/** A period that ends with a report card, with its year. */
export interface ReportPeriodOption {
  id: number;
  label: string;
  period: GradingPeriod;
}
