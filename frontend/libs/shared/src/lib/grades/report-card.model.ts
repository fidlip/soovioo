import {
  BehaviorClassificationValue,
  SchoolHalfYear,
} from '../behavior/behavior.model';
import { Gender } from '../evidence/student.model';

/** How a stupeň is written on the report card: "výborný" or "1". */
export type ReportCardMarkFormat = 'Words' | 'Digits';

export interface ReportCardSettings {
  markFormat: ReportCardMarkFormat;
}

/** Graded = a stupeň 1–5; Exempt = uvolněn/a; NotGraded = nehodnocen/a. */
export type ReportCardGradeKind = 'Graded' | 'Exempt' | 'NotGraded';

export type ReportCardResult =
  'WithHonours' | 'Passed' | 'Failed' | 'NotAssessed';

export interface ReportCardSubject {
  subjectId: number;
  subjectName: string;
  /** Weighted average of the half-year's grades; null without any. */
  average: number | null;
  gradeCount: number;
  /** Rounded average; staff only. */
  suggested: number | null;
  /** With `value`: the grade on the report card — changed, confirmed or the
   * suggestion. Null without any, and for families until it is confirmed
   * and issued. */
  kind: ReportCardGradeKind | null;
  value: number | null;
  /** Changed by a teacher, not confirmed yet. */
  changed: boolean;
  confirmed: boolean;
  confirmedByName: string | null;
  confirmedAt: string | null;
  canEdit: boolean;
  canReopen: boolean;
}

export interface ReportCard {
  studentId: number;
  studentName: string;
  gender: Gender | null;
  schoolClassId: number;
  schoolClassName: string;
  behavior: BehaviorClassificationValue;
  excusedHours: number;
  unexcusedHours: number;
  /** Once every subject is confirmed (families: once issued). */
  result: ReportCardResult | null;
  /** DateOnly "yyyy-MM-dd". */
  issuedOn: string;
  /** The school's day has reached issuedOn. */
  issued: boolean;
  subjects: ReportCardSubject[];
}

export interface ReportCardQuery {
  schoolYearStartYear: number;
  halfYear: SchoolHalfYear;
  schoolClassId?: number | null;
  studentId?: number | null;
  subjectId?: number | null;
}

export interface ReportCardClass {
  id: number;
  name: string;
  /** The caller is its třídní. */
  own: boolean;
}

/** `all` = the whole school (a class must be picked); otherwise all of
 * `classes` may be shown at once. */
export interface ReportCardClasses {
  all: boolean;
  classes: ReportCardClass[];
}

export interface ReportCardGradeKey {
  studentId: number;
  subjectId: number;
}

export interface ConfirmReportCardGradesResult {
  confirmed: number;
  skipped: number;
}
