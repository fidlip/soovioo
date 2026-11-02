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
  /** Days the teacher can teach. */
  teachingDays: DayOfWeek[];
  /** Classes whose class teacher the person's account is. */
  classTeacherOf: string[];
}

export interface ParentProfile {
  children: { studentId: number; name: string }[];
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
    teachingDays: DayOfWeek[];
  };
  parent?: { childIds: number[] };
  student?: StudentMatrika;
}
