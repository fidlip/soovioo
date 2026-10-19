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

export interface SuperAdmin {
  id: number;
  username: string;
}

export interface CreateSuperAdminRequest {
  username: string;
  password: string;
}
