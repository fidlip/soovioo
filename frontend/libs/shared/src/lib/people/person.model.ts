import { RoleRef } from '../auth/role.model';
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
  /** Aprobace. */
  qualification: string | null;
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
  teacher?: { qualification?: string };
  parent?: { childIds: number[] };
  student?: StudentMatrika;
}
