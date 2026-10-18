export interface AppUser {
  id: number;
  username: string;
  firstName: string;
  lastName: string;
  /** Display name ("Jana Nováková", or the username). */
  name: string;
  /** The active role (what the account currently acts as). */
  role: string;
  /** Every role the account may switch between. */
  roles: string[];
  phone: string | null;
  email: string | null;
  /** Set only for role Student — the one Student this account logs in as. */
  studentId: number | null;
  /** Teachers' aprobace (TeacherProfile). */
  qualification: string | null;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  firstName?: string;
  lastName?: string;
  /** At least one; 'Student' can't be combined with others. */
  roles: string[];
  phone?: string;
  email?: string;
  /** Required when roles include 'Student'. */
  studentId?: number;
}

export interface UpdateUserRequest {
  username: string;
  firstName?: string;
  /** Empty keeps the current surname. */
  lastName?: string;
  roles: string[];
  /** Omit to keep the current password. */
  password?: string;
  phone?: string;
  email?: string;
  /** Required when roles include 'Student'. */
  studentId?: number;
}

/** Czech UI names of the roles (the Student one still goes through
 * `localizeStudentTerm` — "žák"/"student" by school type). */
export const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: 'Superadmin',
  Admin: 'Admin',
  Teacher: 'Učitel',
  Parent: 'Rodič',
  Student: 'Žák',
};
