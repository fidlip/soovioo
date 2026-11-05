import { RoleRef } from '../auth/role.model';
import { Qualification } from '../auth/teacher.model';
import { DayOfWeek } from '../timetable/timetable-entry.model';
import { CreateStudentRequest, Student } from '../evidence/student.model';

/** Someone at the school — staff, a parent, a pupil — with the profiles
 * their roles need, with or without an account (Administrace → Osoby). */
export interface Person {
  id: number;
  firstName: string;
  lastName: string;
  name: string;
  phone: string | null;
  email: string | null;
  /** Photo token — the pupil's for a pupil (`studentPhotoUrl`), the
   * account's otherwise (`userPhotoUrl`). */
  photo: string | null;
  account: PersonAccount | null;
  teacher: TeacherProfile | null;
  parent: ParentProfile | null;
  /** The pupil's matrika. */
  student: Student | null;
}

export interface PersonAccount {
  userId: number;
  username: string;
  roles: RoleRef[];
  deactivatedAt: string | null;
}

export interface TeacherProfile {
  /** The aprobace as text, e.g. "D 6.–9., Čj, 1. stupeň". */
  qualification: string | null;
  qualifications: Qualification[];
  /** What the subject list can't say. */
  qualificationNote: string | null;
  /** Nasmlouvané hodiny týdně. */
  contractedHours: number | null;
  /** Days not off as a whole. */
  teachingDays: DayOfWeek[];
  availability: AvailabilitySlot[];
  freeDays: number | null;
  /** Null = the school's, see TeacherWishes. */
  minLessonsPerDay: number | null;
  maxLessonsPerDay: number | null;
  maxGapsPerDay: number | null;
  /** Classes whose class teacher the person's account is. */
  classTeacherOf: string[];
  classTeacherOfIds: number[];
  /** Classes with a lesson of theirs in today's timetable. */
  teachesClassIds: number[];
}

/** Nemůže / raději ne; a slot not listed is free. */
export type AvailabilityKind = 'Unavailable' | 'Avoid';

/** period null = the whole day. */
export interface AvailabilitySlot {
  day: DayOfWeek;
  period: number | null;
  kind: AvailabilityKind;
}

/** What the generator tries to give a teacher on a day with lessons;
 * null = no wish. The school's apply where a teacher has none. */
export interface TeacherWishes {
  minLessonsPerDay: number | null;
  maxLessonsPerDay: number | null;
  maxGapsPerDay: number | null;
}

export interface ParentProfile {
  children: { studentId: number; name: string; schoolClassId: number }[];
}

/** The matrika without the name, which is the person's. */
export type StudentMatrika = Omit<
  CreateStudentRequest,
  'firstName' | 'lastName'
>;

/** A profile left out is removed — except a pupil's, which goes only with
 * the pupil (delete or Ukončení vzdělávání). */
export interface SavePersonRequest {
  firstName: string;
  lastName: string;
  phone?: string;
  email?: string;
  teacher?: {
    qualifications: Qualification[];
    qualificationNote?: string;
    contractedHours?: number | null;
    availability: AvailabilitySlot[];
    freeDays?: number | null;
    minLessonsPerDay?: number | null;
    maxLessonsPerDay?: number | null;
    maxGapsPerDay?: number | null;
  };
  parent?: { childIds: number[] };
  student?: StudentMatrika;
}
