import { RoleRef } from './role.model';

export interface AppUser {
  id: number;
  username: string;
  firstName: string;
  lastName: string;
  /** Display name ("Jana Nováková", or the username). */
  name: string;
  /** The active role (what the account currently acts as). */
  activeRoleId: number | null;
  /** Every role the account may switch between. */
  roles: RoleRef[];
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
  /** Set = the account can't log in (the person left). */
  deactivatedAt?: string | null;
  /** Whose account it is (Administrace → Osoby); name and contacts are
   * that person's. */
  personId: number | null;
}

export type InviteOutcome = 'Sent' | 'NotConfigured' | 'Failed';

export interface CreateUserRequest {
  username: string;
  /** Optional when the person has an e-mail — the account is then usable
   * only through the invitation e-mailed right away. */
  password?: string;
  /** At least one; a pupil's role can't be combined with others. */
  roleIds: number[];
  /** The person must have the profile each role needs (Teacher, Parent,
   * Student — see Person). */
  personId: number | null;
}

export interface UpdateUserRequest {
  username: string;
  roleIds: number[];
  personId: number | null;
  /** Omit to keep the current password. */
  password?: string;
}
