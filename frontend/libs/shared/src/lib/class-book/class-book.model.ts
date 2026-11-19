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
  /** The teacher went through the pupils. */
  attendanceTaken: boolean;
  /** The pupils not there the whole lesson; the rest were. */
  marks: LessonMark[];
}

/** Only `Absent` is an absence in Docházka; `Away` is for the school. */
export type LessonAttendanceStatus = 'Absent' | 'Late' | 'LeftEarly' | 'Away';

export interface LessonMark {
  studentId: number;
  studentName: string;
  status: LessonAttendanceStatus;
  /** How late, or how long before the end they left. */
  minutes: number | null;
}

export interface LessonPupil {
  studentId: number;
  firstName: string;
  lastName: string;
  /** Photo token, when the caller may see it. */
  photo: string | null;
  /** Null = there (or, before the attendance is taken, not known). */
  status: LessonAttendanceStatus | null;
  minutes: number | null;
  /** Why the pupil is likely away ("Omluvenka: nemoc"). */
  suggestion: string | null;
}

export interface LessonPupils {
  /** False: `suggestion` says who likely isn't there. */
  attendanceTaken: boolean;
  pupils: LessonPupil[];
}

export interface LessonMarkRequest {
  studentId: number;
  status: LessonAttendanceStatus;
  minutes: number | null;
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
  /** Empty takes back a record without attendance. */
  topic: string;
  /** The pupils not there the whole lesson; null leaves it as it is. */
  attendance: LessonMarkRequest[] | null;
}
