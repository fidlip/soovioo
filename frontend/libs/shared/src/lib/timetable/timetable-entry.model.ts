export type DayOfWeek =
  | 'Sunday'
  | 'Monday'
  | 'Tuesday'
  | 'Wednesday'
  | 'Thursday'
  | 'Friday'
  | 'Saturday';

export type WeekParity = 'Every' | 'Odd' | 'Even';

export interface TimetableEntry {
  id: number;
  schoolClassId: number;
  subjectId: number;
  subjectName: string;
  /** e.g. "Čj" — null until an admin fills it in on the Subject itself. */
  subjectAbbreviation: string | null;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
  teacherId: number | null;
  teacherName: string | null;
}

export interface CreateTimetableEntryRequest {
  subjectId: number;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
  teacherId?: number | null;
}

/** Same shape as TimetableEntry, plus which class each entry belongs to —
 * needed here because a teacher's entries can span several different
 * classes (unlike the class-scoped TimetableEntry above). */
export interface TeacherTimetableEntry {
  id: number;
  schoolClassId: number;
  schoolClassName: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
  teacherId: number | null;
  teacherName: string | null;
}
