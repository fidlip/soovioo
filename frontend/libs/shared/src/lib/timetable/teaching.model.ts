import { formatNumberWithoutIntl } from '../common/date-format';
import { DayOfWeek } from './timetable-entry.model';

/** Učební plán: hours a week of a subject in a grade (ročník). */
export interface CurriculumItem {
  grade: number;
  subjectId: number;
  hoursPerWeek: number;
  /** In blocks of two; null = as the subject. */
  doubleLessons?: boolean | null;
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
  /** In blocks of two: the class's, its grade's or the subject's. */
  doubleLessons: boolean;
  /** What the grade (or the subject) says. */
  gradeDoubleLessons: boolean;
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
  /** What the teacher is given, class by class. */
  taught: TeachingTaught[];
  problems: string[];
}

export interface TeachingTaught {
  schoolClassId: number;
  schoolClassName: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  hours: number;
  automatic: boolean;
}

/** One teacher's přidělení in a version. */
export interface TeacherTeaching {
  versionId: number;
  versionName: string;
  teacher: TeachingTeacher;
}

/** "6.A M 4 h, 6.B M 4 h". */
export function taughtSummary(taught: TeachingTaught[]): string {
  return taught
    .map(
      (t) =>
        `${t.schoolClassName} ${t.subjectAbbreviation ?? t.subjectName} ${formatHours(t.hours)}`,
    )
    .join(', ');
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

const DAY_SHORT: Record<DayOfWeek, string> = {
  Monday: 'Po',
  Tuesday: 'Út',
  Wednesday: 'St',
  Thursday: 'Čt',
  Friday: 'Pá',
  Saturday: 'So',
  Sunday: 'Ne',
};
const WEEKDAYS: DayOfWeek[] = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
];

/** "Po–Pá", "Po, St, Pá". */
export function teachingDaysLabel(days: DayOfWeek[]): string {
  if (days.length === 5 && WEEKDAYS.every((d) => days.includes(d))) {
    return 'Po–Pá';
  }
  return (
    (Object.keys(DAY_SHORT) as DayOfWeek[])
      .filter((d) => days.includes(d))
      .map((d) => DAY_SHORT[d])
      .join(', ') || '—'
  );
}
