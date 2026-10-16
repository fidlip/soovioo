/** Školní "zvonění" — čas od-do pro dané číslo hodiny, společný pro celou
 * školu. Times are "HH:mm:ss" strings (System.Text.Json's default TimeOnly
 * format) — truncate to "HH:mm" for display/<input type="time">. */
export interface SchoolPeriod {
  periodNumber: number;
  startTime: string;
  endTime: string;
}

export interface UpsertSchoolPeriodRequest {
  startTime: string;
  endTime: string;
}
