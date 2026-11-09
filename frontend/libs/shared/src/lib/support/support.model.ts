/** Result of entering a school in support mode — `code` is a one-time code
 * (valid 1 minute) the school's own page redeems via
 * `SupportApiService.redeem` for an Admin session in that school. */
export interface SupportSessionStart {
  code: string;
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
  /** "Zapsána známka – Jan Novák"; null = show method + path. */
  description: string | null;
}

/** Audit log row: a SuperAdmin's visit to a school and what they changed. */
export interface SupportSession {
  id: number;
  superAdminUsername: string;
  schoolId: number;
  schoolName: string;
  startedAt: string;
  endedAt: string | null;
  /** The title of the ticket it was entered for. */
  purpose: string | null;
  /** What was done, written on leaving (Markdown). */
  result: string | null;
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

/** When a SuperAdmin may enter the school — the school's choice. Entering
 * is always for a ticket, by whoever takes care of it. */
export type SupportAccess = 'Always' | 'Approval' | 'Never';

/** Administrace → Podpora: the policy. */
export interface SchoolSupport {
  access: SupportAccess;
  /** How long allowing entering for a ticket lasts. */
  approvalMinutes: number;
}

/** `1440` → `24 hodin`, `2` → `2 minuty` — for "na …". */
export function approvalDuration(minutes: number): string {
  const [n, one, few, many] =
    minutes % 60 === 0
      ? [minutes / 60, 'hodinu', 'hodiny', 'hodin']
      : [minutes, 'minutu', 'minuty', 'minut'];
  return `${n} ${n === 1 ? one : n >= 2 && n <= 4 ? few : many}`;
}

export type SupportTicketStatus =
  | 'New'
  /** Has a SuperAdmin taking care of it. */
  | 'Assigned'
  /** Being worked on right now. */
  | 'InProgress'
  /** The support asked; the school's answer brings it back to Assigned. */
  | 'WaitingForSchool'
  | 'Resolved'
  | 'Cancelled';

export const SUPPORT_TICKET_STATUS_LABELS: Record<SupportTicketStatus, string> =
  {
    New: 'Nový',
    Assigned: 'Přiřazeno',
    InProgress: 'Řeší se',
    WaitingForSchool: 'Čeká na školu',
    Resolved: 'Vyřešeno',
    Cancelled: 'Zrušeno',
  };

export function isTicketOpen(status: SupportTicketStatus): boolean {
  return status !== 'Resolved' && status !== 'Cancelled';
}

/** A case of support for a school — asked by its admin or opened by a
 * SuperAdmin; due by the school's support plan. */
export interface SupportTicket {
  id: number;
  schoolId: number;
  schoolName: string;
  /** The school's policy. */
  schoolAccess: SupportAccess;
  title: string;
  /** Markdown. */
  description: string;
  createdAt: string;
  createdByName: string;
  /** Opened by a SuperAdmin for the school. */
  createdBySupport: boolean;
  /** The school's plan when it was created. */
  planName: string;
  /** `yyyy-MM-dd`. */
  dueDate: string;
  status: SupportTicketStatus;
  statusChangedAt: string | null;
  assigneeUserId: number | null;
  assigneeName: string | null;
  /** Still open after `dueDate`. */
  overdue: boolean;
  /** Open and the assignee waits for the school to allow entering. */
  accessRequestedAt: string | null;
  /** Open and entering allowed until then; null otherwise. */
  accessUntil: string | null;
}

export type SupportTicketEntryKind =
  | 'Message'
  /** The SuperAdmins' own — the school doesn't see it. */
  | 'Note'
  | 'Created'
  | 'Assigned'
  /** Back to the pool, with a summary (internal). */
  | 'Released'
  | 'StatusChanged'
  | 'AccessRequested'
  | 'AccessGranted'
  | 'AccessRevoked'
  /** A support session — text is what was done. */
  | 'Visit';

export interface SupportTicketEntry {
  id: number;
  at: string;
  kind: SupportTicketEntryKind;
  authorName: string;
  bySupport: boolean;
  internal: boolean;
  /** Markdown. */
  text: string | null;
  status: SupportTicketStatus | null;
  until: string | null;
}

export interface SupportTicketDetail {
  ticket: SupportTicket;
  entries: SupportTicketEntry[];
}

export type SupportTicketFilter = 'Open' | 'Mine' | 'Unassigned' | 'All';
