export interface ClassBookClass {
  id: number;
  name: string;
}

/** A lesson of the day; `date`, `periodNumber`, `schoolClassId` and
 * `studentGroupId` say which. */
export interface ClassBookLesson {
  date: string;
  periodNumber: number;
  /** Null for a group's lesson. */
  schoolClassId: number | null;
  studentGroupId: number | null;
  startTime: string | null;
  endTime: string | null;
  /** "6.A", or "Aj 1 (6.A, 6.B)" for a group. */
  who: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  teacherId: number | null;
  teacherName: string | null;
  classroomName: string | null;
  /** "Supl", "Odpadá", …; empty for the timetable's lesson. */
  label: string;
  /** Not taught: cancelled or moved elsewhere. */
  gone: boolean;
  /** Null while not written. */
  topic: string | null;
  recordedByName: string | null;
  recordedAt: string | null;
  /** The caller may write it now. */
  canWrite: boolean;
}

export interface ClassBookDay {
  date: string;
  lessons: ClassBookLesson[];
  /** Why there are no lessons ("Podzimní prázdniny"). */
  dayOff: string | null;
}

export interface SaveLessonRequest {
  date: string;
  periodNumber: number;
  schoolClassId: number | null;
  studentGroupId: number | null;
  /** Empty takes the record back. */
  topic: string;
}
