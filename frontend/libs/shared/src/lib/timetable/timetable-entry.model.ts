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
