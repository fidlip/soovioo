export type PeriodReport = 'None' | 'Interim' | 'ReportCard';

/** A part of a school year ("období": pololetí). */
export interface GradingPeriod {
  id: number;
  name: string;
  /** With `to`: the days it covers — the year's last one to the year's end. */
  from: string;
  to: string;
  report: PeriodReport;
  /** The day its report card is handed out. */
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
  /** Today is in it. */
  current: boolean;
  periods: GradingPeriod[];
}

export interface SchoolYearDates {
  teachingStart: string;
  firstHalfIssuedOn: string;
  secondHalfIssuedOn: string;
}

/** The year to found next, with the dates to start from. */
export interface SchoolYearSuggestion extends SchoolYearDates {
  startYear: number;
  name: string;
  /** The dates are MŠMT's; otherwise an estimate. */
  official: boolean;
}

export interface CreateSchoolYearRequest extends SchoolYearDates {
  startYear: number;
}

/** A period that ends with a report card, with its year. */
export interface ReportPeriodOption {
  id: number;
  label: string;
  period: GradingPeriod;
}
