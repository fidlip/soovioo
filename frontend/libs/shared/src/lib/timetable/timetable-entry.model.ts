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
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
}

export interface CreateTimetableEntryRequest {
  subjectId: number;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
}
