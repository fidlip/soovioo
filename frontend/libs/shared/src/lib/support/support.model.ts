/** Result of entering a school in support mode — `token` is a 2-hour Admin
 * token for that school (no refresh). */
export interface SupportSessionStart {
  token: string;
  sessionId: number;
  schoolSlug: string;
  schoolName: string;
  expiresAt: string;
}

export interface SupportAction {
  method: string;
  path: string;
  statusCode: number;
  at: string;
}

/** Audit log row: a SuperAdmin's visit to a school and what they changed. */
export interface SupportSession {
  id: number;
  superAdminUsername: string;
  schoolId: number;
  schoolName: string;
  startedAt: string;
  endedAt: string | null;
  actions: SupportAction[];
}

/** A SuperAdmin account — an ordinary user without a school, with the same
 * name/contact fields (Person) as everyone else. */
export interface SuperAdmin {
  id: number;
  username: string;
  firstName: string;
  lastName: string;
  /** Display name ("Jana Nová", or the username). */
  name: string;
  phone: string | null;
  email: string | null;
  /** The seeded "superadmin" account — can't be deleted or renamed. */
  builtIn: boolean;
}

export interface CreateSuperAdminRequest {
  username: string;
  password: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  email?: string;
}

/** `password` empty/omitted = keep the current one. */
export interface UpdateSuperAdminRequest {
  username: string;
  password?: string;
  firstName?: string;
  lastName?: string;
  phone?: string;
  email?: string;
}
