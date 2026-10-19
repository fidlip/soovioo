/** One audited change (or login/logout) — see backend AuditLogController. */
export interface AuditLogEntry {
  id: number;
  at: string;
  schoolId: number | null;
  schoolName: string | null;
  userId: number | null;
  /** For a failed login the username that was tried. */
  username: string | null;
  userDisplayName: string | null;
  role: string | null;
  /** Made in support mode by this SuperAdmin. */
  supportBy: string | null;
  method: string;
  path: string;
  /** Agenda label ("Známky", "Uživatelé"…). */
  area: string;
  /** "Vytvořen uživatel Karel Nový"; null = show method + path. */
  description: string | null;
  statusCode: number;
  /** Request payload, passwords/tokens masked. */
  requestBody: string | null;
  /** Error message, or the new record's id. */
  result: string | null;
  ipAddress: string | null;
}

export interface AuditLogOption {
  id: number;
  name: string;
}

export interface AuditLogFilterOptions {
  areas: string[];
  users: AuditLogOption[];
  /** Only with a school (always for a school admin). */
  classes: AuditLogOption[];
}

export interface AuditLogQuery {
  schoolId?: number | null;
  schoolClassId?: number | null;
  userId?: number | null;
  area?: string | null;
  /** ISO instants; `to` is exclusive. */
  from?: string | null;
  to?: string | null;
  page?: number;
  pageSize?: number;
}
