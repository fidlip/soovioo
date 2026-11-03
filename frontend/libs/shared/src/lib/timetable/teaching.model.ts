import { formatNumberWithoutIntl } from '../common/date-format';
import { DayOfWeek } from './timetable-entry.model';

/** Učební plán: hours a week of a subject in a grade (ročník). */
export interface CurriculumItem {
  grade: number;
  subjectId: number;
  hoursPerWeek: number;
}

/** A subject in a class: the plan, the timetable and who teaches it. */
export interface TeachingRow {
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  /** By the class's plan; null = not in it, 0 = the class doesn't have it. */
  planHours: number | null;
  /** What the grade's plan says. */
  gradeHours: number | null;
  /** The class has its own hours, not its grade's. */
  classPlan: boolean;
  timetableHours: number;
  teacherId: number | null;
  teacherName: string | null;
  /** Chosen by the system, not by the school. */
  automatic: boolean;
  problems: string[];
}

export interface TeachingClass {
  id: number;
  name: string;
  /** Ročník; null = the plan doesn't apply. */
  grade: number | null;
  problems: string[];
  rows: TeachingRow[];
}

export interface TeachingTeacher {
  id: number;
  name: string;
  /** The aprobace as text. */
  qualification: string | null;
  contractedHours: number | null;
  assignedHours: number;
  timetableHours: number;
  teachingDays: DayOfWeek[];
  problems: string[];
}

/** Učební plán and přidělení výuky of one timetable version. */
export interface TeachingOverview {
  versionId: number;
  canEdit: boolean;
  /** Ročníky of the school's classes and of the plan. */
  grades: number[];
  curriculum: CurriculumItem[];
  classes: TeachingClass[];
  teachers: TeachingTeacher[];
}

/** "1 h", "1,5 h". */
export function formatHours(hours: number): string {
  return `${formatNumberWithoutIntl(hours)} h`;
}
