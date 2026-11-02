/** What of a former pupil gets erased, a category at a time — see
 * RetentionPolicy on the backend. `Profile` ends it all. */
export type RetentionCategory =
  | 'Photos'
  | 'Contacts'
  | 'Messages'
  | 'Homework'
  | 'Behavior'
  | 'Attendance'
  | 'Grades'
  | 'Payments'
  | 'Profile';

/** Confirm: due erasures wait for an admin. Automatic: the daily job does them. */
export type ErasureMode = 'Confirm' | 'Automatic';

/** Who's told of a departure — the pupil's own account, their parents,
 * the class teacher. */
export type LeaveNoticeGroup = 'Student' | 'Parents' | 'ClassTeacher';

export interface LeaveNotice {
  group: LeaveNoticeGroup;
  /** The message names the reason. */
  withReason: boolean;
}

export type StudentLeaveReason =
  'Transfer' | 'Graduated' | 'Expelled' | 'Other';

export interface RetentionPolicy {
  mode: ErasureMode;
  /** Days after the last day before the pupil's and parents' accounts stop working. */
  accountGraceDays: number;
  /** Years after the pupil's last day, per category (0 = right away). */
  photosYears: number;
  contactsYears: number;
  messagesYears: number;
  homeworkYears: number;
  behaviorYears: number;
  attendanceYears: number;
  gradesYears: number;
  paymentsYears: number;
  profileYears: number;
  /** Years after a staff account was deactivated. */
  staffYears: number;
  /** Whom a member of staff's departure is announced to by default. */
  staffLeaveNotices: StaffLeaveNoticeGroup[];
}

export interface ScheduledErasure {
  category: RetentionCategory;
  on: string;
}

export interface FormerStudent {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassName: string;
  /** The pupil's last day at the school. */
  leftAt: string;
  reason: StudentLeaveReason | null;
  /** The reason in words, with `Other`. */
  reasonDetail: string | null;
  /** For the school only. */
  note: string | null;
  /** The last day is over (false = departure announced). */
  former: boolean;
  /** The pupil's own account still works. */
  accountActive: boolean;
  erased: RetentionCategory[];
  /** Erasable now. */
  due: RetentionCategory[];
  /** Not erased yet, with the day each becomes due. */
  pending: ScheduledErasure[];
}

export interface FormerStaff {
  userId: number;
  name: string;
  /** Null = the departure is announced, the account still works. */
  deactivatedAt: string | null;
  /** The last day, when recorded (Ukončení pracovního poměru). */
  leftAt: string | null;
  erasesOn: string | null;
  due: boolean;
  /** Třídnictví still theirs — not handed over. */
  classesLed: number;
  /** Their lessons in the timetable, not handed over. */
  lessons: number;
}

export type StaffLeaveReason =
  'Agreement' | 'Notice' | 'FixedTermEnd' | 'Retirement' | 'Other';

/** Pupils and parents of the classes they lead / teach, the other staff. */
export type StaffLeaveNoticeGroup =
  'LedClasses' | 'TaughtClasses' | 'Colleagues';

export interface ClassSuccessor {
  classId: number;
  successorId: number;
}

export interface SubjectSubstitute {
  subjectId: number;
  substituteId: number;
}

export interface StaffLeavePreview {
  /** `YYYY-MM-DD`, null = no departure yet. */
  lastDay: string | null;
  reason: StaffLeaveReason | null;
  note: string | null;
  ledClasses: { classId: number; name: string; successorId: number | null }[];
  subjects: {
    subjectId: number;
    name: string;
    /** Their lessons of it in a week. */
    lessons: number;
    classes: string[];
    substituteId: number | null;
    /** Colleagues whose aprobace covers it in those classes. */
    qualifiedIds: number[];
  }[];
  /** Colleagues to hand things over to. */
  staff: { userId: number; name: string }[];
  /** The school's default — only for a new departure. */
  noticeDefaults: StaffLeaveNoticeGroup[] | null;
}

export interface StaffLeaveRequest {
  lastDay: string;
  reason: StaffLeaveReason | null;
  note: string | null;
  classTeachers: ClassSuccessor[];
  substitutes: SubjectSubstitute[];
  notices: StaffLeaveNoticeGroup[];
}

/** A handed-over lesson at a time the substitute already teaches. */
export interface HandoverConflict {
  subjectId: number;
  substituteId: number;
  substituteName: string;
  /** `Monday` … `Sunday`. */
  dayOfWeek: string;
  periodNumber: number;
  class: string;
  otherClass: string;
}

export interface RetentionOverview {
  students: FormerStudent[];
  staff: FormerStaff[];
}

export interface LeaveRequest {
  leftAt: string;
  reason: StudentLeaveReason | null;
  /** Required with `Other`. */
  reasonDetail: string | null;
  /** For the school only. */
  note: string | null;
  /** Who's told (only of a new departure or a new last day). */
  notices: LeaveNotice[];
}

export interface EraseDueResult {
  students: number;
  staff: number;
}
