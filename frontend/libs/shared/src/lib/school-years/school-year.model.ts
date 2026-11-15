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
}

/** The year to found next, prefilled from MŠMT's calendar. */
export interface SchoolYearSuggestion extends SchoolYearRequest {
  /** The dates are MŠMT's; otherwise an estimate. */
  official: boolean;
}

/** A period that ends with a report card, with its year. */
export interface ReportPeriodOption {
  id: number;
  label: string;
  period: GradingPeriod;
}
