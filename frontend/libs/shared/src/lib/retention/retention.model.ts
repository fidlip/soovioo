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
  deactivatedAt: string;
  erasesOn: string;
  due: boolean;
}

export interface RetentionOverview {
  students: FormerStudent[];
  staff: FormerStaff[];
}

export interface LeaveRequest {
  leftAt: string;
  reason: StudentLeaveReason | null;
  note: string | null;
}

export interface EraseDueResult {
  students: number;
  staff: number;
}
