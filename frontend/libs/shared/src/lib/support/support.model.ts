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
  /** The caller is the school's admin (not the support inside it) — only
   * they change `access`. */
  canChange: boolean;
}

export const SUPPORT_ACCESS_LABELS: Record<SupportAccess, string> = {
  Approval: 'Po schválení',
  Always: 'Kdykoliv',
  Never: 'Nikdy',
};

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
  /** Done by the support — the school closes or returns it; closes by
   * itself after 30 days. */
  | 'Resolved'
  | 'Cancelled'
  /** Confirmed by the school, or closed by itself. */
  | 'Closed';

export const SUPPORT_TICKET_STATUS_LABELS: Record<SupportTicketStatus, string> =
  {
    New: 'Nový',
    Assigned: 'Přiřazeno',
    InProgress: 'Řeší se',
    WaitingForSchool: 'Čeká na školu',
    Resolved: 'Vyřešeno',
    Cancelled: 'Zrušeno',
    Closed: 'Uzavřeno',
  };

export function isTicketOpen(status: SupportTicketStatus): boolean {
  return status !== 'Resolved' && status !== 'Cancelled' && status !== 'Closed';
}

/** The school's support plan: how many working days a ticket may take. */
export interface SupportPlan {
  code: string;
  name: string;
  responseWorkingDays: number;
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
  /** Resolved: closes by itself then. */
  autoClosesAt?: string | null;
  /** Closed or cancelled and may still be reopened till then. */
  reopenableUntil?: string | null;
  /** In the detail for the school: the caller may close or return it
   * (resolved), or reopen it. */
  canConfirm?: boolean;
  /** In the detail: the support's changes not reset yet, when the caller
   * may reset them; 0 otherwise. */
  resettableChanges?: number;
}

export type SupportTicketEntryKind =
  | 'Message'
  /** The SuperAdmins' own — the school doesn't see it. */
  | 'Note'
  | 'Created'
  | 'Assigned'
  /** Back to the pool, with a summary (internal). */
  | 'Released'
  /** Given to another SuperAdmin — text is their name. */
  | 'HandedOver'
  /** With Cancelled, text is why. */
  | 'StatusChanged'
  | 'AccessRequested'
  | 'AccessGranted'
  | 'AccessRevoked'
  /** Leaving the school ("Opustit") — text is what was done. */
  | 'Visit'
  /** The support's changes reset — text says how it went; sessionId when
   * of one session. */
  | 'Reset'
  // The ticket's support sessions, put in by the API (id 0):
  | 'SessionStarted'
  /** A change in the school — text says it in words. */
  | 'SessionAction'
  /** Ended without leaving — allowing taken back, the ticket closed. */
  | 'SessionEnded'
  | 'SessionExpired';

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
  /** The support session it belongs to. */
  sessionId: number | null;
  /** With SessionAction. */
  statusCode: number | null;
  /** With SessionAction, "METHOD /path" — only for the SuperAdmins. */
  request: string | null;
  /** With SessionStarted: the session's last request. */
  lastSeenAt?: string | null;
  /** With SessionStarted: the session's changes not reset yet, when the
   * caller may reset them. */
  changes?: number;
  /** A message or note of the caller's side — they may change or delete
   * it (a SuperAdmin also retype it). */
  canEdit?: boolean;
  /** Changed afterwards — only for the entry's side and the SuperAdmins,
   * like deleted entries and the revisions. */
  editedAt?: string | null;
  /** Deleted (text null — the revisions have it). */
  deleted?: boolean;
  /** Oldest first. */
  revisions?: SupportTicketEntryRevision[] | null;
}

export type SupportTicketEntryChange =
  'Edited' | 'Deleted' | 'MadeInternal' | 'MadePublic';

export const SUPPORT_TICKET_ENTRY_CHANGE_LABELS: Record<
  SupportTicketEntryChange,
  string
> = {
  Edited: 'upraveno',
  Deleted: 'smazáno',
  MadeInternal: 'změněno na interní poznámku',
  MadePublic: 'posláno škole',
};

export interface SupportTicketEntryRevision {
  at: string;
  change: SupportTicketEntryChange;
  authorName: string;
  /** Markdown — the text before the change. */
  text: string | null;
}

/** Where a ticket is worked on: by the SuperAdmins, by one inside the
 * school (the visit's ticket), by the school. */
export type SupportTicketScope = 'support' | 'session' | 'school';

export interface SupportTicketDetail {
  ticket: SupportTicket;
  entries: SupportTicketEntry[];
}

export type SupportTicketFilter = 'Open' | 'Mine' | 'Unassigned' | 'All';

/** "1 změna", "3 změny", "5 změn". */
export function changesLabel(count: number): string {
  return `${count} ${count === 1 ? 'změna' : count >= 2 && count <= 4 ? 'změny' : 'změn'}`;
}

/** Added by the support — goes; deleted — comes back; changed — gets its
 * values back. */
export type SupportResetAction = 'Delete' | 'Restore' | 'Revert';

export const SUPPORT_RESET_ACTION_LABELS: Record<SupportResetAction, string> = {
  Delete: 'smaže se',
  Restore: 'obnoví se',
  Revert: 'vrátí se původní hodnoty',
};

export interface SupportResetItem {
  /** "Známka". */
  type: string;
  /** "5.A", "Jana Nováková", "#12". */
  record: string;
  action: SupportResetAction;
  /** Why it stays as it is; null = it's returned. */
  skipped: string | null;
  sessionIds: number[];
}

export interface SupportResetPreview {
  items: SupportResetItem[];
}
