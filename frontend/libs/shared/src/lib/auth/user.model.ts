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
  /** Only in the response to creating an account with an e-mail — what
   * became of the invitation sent along with it. */
  invite?: InviteOutcome;
  /** Photo token for `PhotosApiService` URLs — present only when the
   * caller may see the photo. */
  photo?: string | null;
}

export type InviteOutcome = 'Sent' | 'NotConfigured' | 'Failed';

export interface CreateUserRequest {
  username: string;
  /** Optional when `email` is set — the account is then usable only through
   * the invitation e-mailed right away. */
  password?: string;
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
