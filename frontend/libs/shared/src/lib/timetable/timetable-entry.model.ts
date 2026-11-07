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
  classroomId: number | null;
  classroomName: string | null;
  /** Same teacher/classroom at the same time elsewhere — empty when none. */
  conflicts: string[];
  /** The generator leaves it where it is. */
  locked: boolean;
  /** A lesson of a group; null = the whole class. */
  studentGroupId?: number | null;
  studentGroupName?: string | null;
  /** Today's change of the lesson (on Home), e.g. "Supl". */
  change?: string | null;
  /** Not taught today. */
  gone?: boolean;
  /** The timetable's subject when another is taught today; likewise the teacher and the classroom. */
  wasSubject?: string | null;
  wasTeacher?: string | null;
  wasClassroom?: string | null;
}

export interface CreateTimetableEntryRequest {
  subjectId: number;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  weekParity: WeekParity;
  teacherId?: number | null;
  classroomId?: number | null;
  /** Null/missing = the version valid today; ignored on update. */
  timetableVersionId?: number | null;
  locked?: boolean;
  /** A lesson of the group, for all its classes; ignored on update. */
  studentGroupId?: number | null;
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
  classroomId: number | null;
  classroomName: string | null;
  /** Same teacher/classroom at the same time elsewhere — empty when none. */
  conflicts: string[];
  /** The generator leaves it where it is. */
  locked: boolean;
  /** A lesson of a group (once, `schoolClassName` names it); null = the whole class. */
  studentGroupId?: number | null;
  studentGroupName?: string | null;
}
