import {
  BehaviorClassificationValue,
  SchoolHalfYear,
} from '../behavior/behavior.model';
import { Gender } from '../evidence/student.model';

/** How a stupeň prospěchu (and chování) is written on the report card:
 * the number "1" or its word "výborný". */
export type ReportCardMarkFormat = 'Words' | 'Digits';

export interface ReportCardSettings {
  markFormat: ReportCardMarkFormat;
}

/** How a subject or behaviour is assessed on the report card: a stupeň,
 * slovní hodnocení (with the stupeň it corresponds to, never printed nor
 * shown to families), or a stupeň with a slovní hodnocení added. */
export type AssessmentMode =
  'Classification' | 'Verbal' | 'ClassificationWithVerbal';

/** The school's rule: a ročník, a class or a pupil (exactly one), all
 * subjects (`subjectId` null), one subject, or behaviour. */
export interface AssessmentModeRule {
  id: number;
  rocnik: number | null;
  schoolClassId: number | null;
  schoolClassName: string | null;
  studentId: number | null;
  studentName: string | null;
  subjectId: number | null;
  subjectName: string | null;
  behavior: boolean;
  mode: AssessmentMode;
  /** Null = as a less specific rule, or the school's default; always null
   * with slovní hodnocení. */
  markFormat: ReportCardMarkFormat | null;
}

export type SetAssessmentModeRule = Omit<
  AssessmentModeRule,
  'id' | 'schoolClassName' | 'studentName' | 'subjectName'
>;

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
  /** The caller grades the pupil in the subject, confirmed or not. */
  mine: boolean;
  canEdit: boolean;
  canReopen: boolean;
  mode: AssessmentMode;
  /** Slovní hodnocení; families once confirmed and issued. */
  verbalText: string | null;
  /** Has what confirming takes (a stupeň, and the text where it goes);
   * staff only. */
  complete: boolean;
}

/** The souhrnné slovní hodnocení of the first half — instead of each
 * subject, when every subject is assessed slovně. */
export interface ReportCardSummary {
  text: string | null;
  result: ReportCardResult | null;
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
  /** Null for families when behaviour is assessed only slovně. */
  behavior: BehaviorClassificationValue | null;
  excusedHours: number;
  unexcusedHours: number;
  /** Once every subject is confirmed (families: once issued). */
  result: ReportCardResult | null;
  /** DateOnly "yyyy-MM-dd". */
  issuedOn: string;
  /** The school's day has reached issuedOn. */
  issued: boolean;
  /** All the pupil's subjects are here — false for a teacher outside
   * their own class, who sees only the subjects they grade and can't print
   * the report card. */
  whole: boolean;
  subjects: ReportCardSubject[];
  behaviorMode: AssessmentMode;
  behaviorText: string | null;
  /** The třídní writes the slovní hodnocení of behaviour. */
  canEditBehaviorText: boolean;
  /** Any slovní hodnocení — the report card goes on lists A/B. */
  hasVerbal: boolean;
  /** Only where it's allowed and the caller sees the whole report card. */
  summary: ReportCardSummary | null;
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
