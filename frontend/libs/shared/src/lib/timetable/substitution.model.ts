import { DayOfWeek } from './timetable-entry.model';

/** A changed lesson as a class, pupil or teacher sees it. */
export interface LessonChange {
  date: string;
  dayOfWeek: DayOfWeek;
  periodNumber: number;
  /** With studentGroupId which lesson of the timetable it is. */
  schoolClassId: number | null;
  studentGroupId: number | null;
  /** Shown instead of that lesson; otherwise besides the timetable's. */
  replaces: boolean;
  /** Not taught (to them) then. */
  gone: boolean;
  label: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  teacherId: number | null;
  teacherName: string | null;
  classroomId: number | null;
  classroomName: string | null;
  schoolClassName: string;
  studentGroupName: string | null;
}

export interface LessonChangeData {
  date: string;
  period: number;
  classId: number | null;
  groupId: number | null;
  extra: boolean;
  cancelled: boolean;
  teacherId: number | null;
  subjectId: number | null;
  classroomId: number | null;
  movedToDate: string | null;
  movedToPeriod: number | null;
  mergedWithClassId: number | null;
  mergedWithGroupId: number | null;
}

export interface TeacherAbsence {
  id: number;
  teacherId: number;
  teacherName: string;
  dateFrom: string;
  dateTo: string;
  periodFrom: number | null;
  periodTo: number | null;
}

export interface TeacherAbsenceRequest {
  teacherId: number;
  dateFrom: string;
  dateTo: string;
  periodFrom: number | null;
  periodTo: number | null;
  /** The day to show afterwards. */
  day: string;
}

/** A lesson of the day as prepared; a moved one also at its new period. */
export interface SubstitutionLesson {
  periodNumber: number;
  schoolClassId: number | null;
  studentGroupId: number | null;
  who: string;
  subjectId: number;
  subject: string;
  teacherId: number | null;
  teacher: string | null;
  classroomId: number | null;
  classroom: string | null;
  originalSubjectId: number | null;
  originalTeacherId: number | null;
  originalTeacherAbsent: boolean;
  /** Its teacher is away and nobody substitutes. */
  needsCover: boolean;
  gone: boolean;
  label: string | null;
  changeId: number | null;
  change: LessonChangeData | null;
  movedIn: boolean;
}

export interface SubstitutionDay {
  date: string;
  hasTimetable: boolean;
  publishedAt: string | null;
  /** The prepared changes differ from the published ones. */
  unpublished: boolean;
  absences: TeacherAbsence[];
  lessons: SubstitutionLesson[];
  problems: string[];
}

export interface LessonChangeRequest {
  date: string;
  periodNumber: number;
  schoolClassId: number | null;
  studentGroupId: number | null;
  extra: boolean;
  cancelled: boolean;
  /** Null = the timetable's; likewise the subject and the classroom. */
  teacherId: number | null;
  subjectId: number | null;
  classroomId: number | null;
  movedToDate: string | null;
  movedToPeriod: number | null;
  mergedWithClassId: number | null;
  mergedWithGroupId: number | null;
}

export interface Substitute {
  teacherId: number;
  name: string;
  qualified: boolean;
  teachesThatDay: boolean;
  substitutionsThisMonth: number;
}

export interface SubstitutionCandidates {
  teachers: Substitute[];
  /** Free then; `suits` = one the subject uses. */
  classrooms: { id: number; name: string; suits: boolean }[];
}

/** Whose week of changes: a class, a pupil or a teacher. */
export interface LessonChangeScope {
  schoolClassId?: number | null;
  teacherId?: number | null;
  studentId?: number | null;
}
