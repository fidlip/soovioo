import { Absence } from '../attendance/absence.model';
import { Grade } from '../grades/grade.model';
import { TimetableEntry } from '../timetable/timetable-entry.model';

export interface UpcomingEvent {
  id: number;
  title: string;
  startAt: string;
  endAt: string;
  allDay: boolean;
}

export interface SchoolCounts {
  studentCount: number;
  teacherCount: number;
  classCount: number;
}

/** One aggregated summary for the Home page — see DashboardController on the
 * backend. Which fields are actually populated depends on the caller's role
 * (Admin gets schoolCounts but no todayLessons/recentGrades, Teacher gets
 * todayLessons but no recentGrades, etc.) — the Home page reads only what's
 * relevant to the current role rather than branching on every field. */
export interface Dashboard {
  todayLessons: TimetableEntry[];
  /** Set only for Parent/Student — which student todayLessons is for. */
  todayLessonsContext: string | null;
  recentGrades: Grade[];
  pendingAbsences: Absence[];
  unreadMessageCount: number;
  upcomingEvents: UpcomingEvent[];
  /** Admin only. */
  schoolCounts: SchoolCounts | null;
}
