import {
  TICKET_STATUS_LABELS,
  TICKET_TIME_CATEGORY_LABELS,
  TicketStatus,
  TicketTimeCategory,
  formatTicketMinutes,
} from '@fidlip/soovioo-ui';
import { RoleRef } from '../auth/role.model';

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
  /** Whom the support enters as. */
  supportAs: SupportAsScope;
  /** The instance has the support (is connected to a backoffice). */
  available: boolean;
}

/** Whom the support enters the school as: its admin, or only a user the
 * ticket names ("Vstoupit jako…"). */
export type SupportAsScope = 'Admin' | 'Users';

export const SUPPORT_AS_SCOPE_LABELS: Record<SupportAsScope, string> = {
  Users: 'Vybraný uživatel',
  Admin: 'Admin školy',
};

/** A user of the school the support may be (or the school may name on a
 * ticket), with their roles. */
export interface SupportAsUser {
  id: number;
  name: string;
  roles: RoleRef[];
}

/** SuperAdmins in the school as someone (support mode, "Vstoupit jako…"):
 * as the caller (`asMe`), or as someone else for the caller's ticket. */
export interface SupportPresence {
  sessionId: number;
  asMe: boolean;
  superAdminName: string;
  /** "učitel Jan Novák". */
  as: string;
  startedAt: string;
  ticketId: number | null;
  ticketTitle: string | null;
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

export type SupportTicketStatus = TicketStatus;

export const SUPPORT_TICKET_STATUS_LABELS = TICKET_STATUS_LABELS;

export function isTicketOpen(status: SupportTicketStatus): boolean {
  return status !== 'Resolved' && status !== 'Cancelled' && status !== 'Closed';
}

/** The school's support plan: how many working days a ticket may take. */
export interface SupportPlan {
  code: string;
  name: string;
  responseWorkingDays: number;
}

/** A school's ticket in its list. */
export interface SupportTicketSummary {
  id: number;
  title: string;
  status: SupportTicketStatus;
  createdAt: string;
  dueDate: string;
  overdue: boolean;
  createdByName: string;
  createdBySupport: boolean;
  assigneeName: string | null;
  lastActivityAt: string;
}

export interface SupportTicket {
  id: number;
  title: string;
  description: string;
  status: SupportTicketStatus;
  createdAt: string;
  statusChangedAt: string | null;
  createdByName: string;
  createdBySupport: boolean;
  planName: string;
  dueDate: string;
  overdue: boolean;
  assigneeName: string | null;
  /** Resolved: closes by itself then. */
  autoClosesAt: string | null;
  /** Closed or cancelled and may still be reopened till then. */
  reopenableUntil: string | null;
  /** The caller may close or return it (resolved), or reopen it. */
  canConfirm: boolean;
  /** The support's changes not reset yet, when the caller may reset them. */
  resettableChanges: number;
  /** The caller is the school's admin: decides the support's estimates. */
  canDecideEstimates: boolean;
}

/** The backoffice's kinds (and the school's: `Visit`, `Reset` and the
 * support sessions, put in by the API with id 0). */
export type SupportTicketEntryKind =
  | 'Message'
  | 'Created'
  | 'Assigned'
  | 'Released'
  | 'HandedOver'
  | 'StatusChanged'
  | 'EstimateRequested'
  | 'AccessRequested'
  | 'AccessGranted'
  | 'AccessRevoked'
  | 'AccessRejected'
  | 'SupportAsChanged'
  | 'TimeCategoryChanged'
  | 'TimeDisputed'
  | 'TimeDisputeDecided'
  | 'EstimateProposed'
  | 'EstimateApproved'
  | 'EstimateRejected'
  | 'Visit'
  | 'Reset'
  | 'SessionStarted'
  | 'SessionAction'
  | 'SessionEnded'
  | 'SessionExpired';

export interface SupportTicketEntry {
  id: number;
  at: string;
  kind: SupportTicketEntryKind;
  authorName: string;
  bySupport: boolean;
  text: string | null;
  status: SupportTicketStatus | null;
  until: string | null;
  sessionId: number | null;
  statusCode: number | null;
  as?: string | null;
  inactiveSince?: string | null;
  changes?: number;
  canEdit?: boolean;
  editedAt?: string | null;
  deleted?: boolean;
}

/** What the time spent on a ticket is: our fault, the plan's hours, or a paid service. */
export type SupportTimeCategory = TicketTimeCategory;

export const SUPPORT_TIME_CATEGORY_LABELS = TICKET_TIME_CATEGORY_LABELS;

export interface SupportTimeDispute {
  id: number;
  status: 'Open' | 'Accepted' | 'Rejected';
  proposedCategory: SupportTimeCategory;
  reason: string;
  byName: string;
  createdAt: string;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
}

/** `category` as logged; `countedAs` differs while a dispute is open. */
export interface SupportTimeEntry {
  id: number;
  date: string;
  minutes: number;
  category: SupportTimeCategory;
  countedAs: SupportTimeCategory;
  operatorName: string;
  dispute: SupportTimeDispute | null;
}

export type SupportEstimateStatus =
  'Pending' | 'Approved' | 'Rejected' | 'Withdrawn';

export interface SupportEstimate {
  id: number;
  category: SupportTimeCategory;
  minutes: number;
  price: number;
  note: string | null;
  operatorName: string;
  createdAt: string;
  status: SupportEstimateStatus;
  decidedByName: string | null;
  decidedAt: string | null;
  decisionNote: string | null;
}

/** The school's support time in a month. */
export interface SupportMonthUsage {
  month: string;
  planName: string | null;
  includedMinutes: number;
  carriedMinutes: number;
  supportMinutes: number;
  remainingMinutes: number;
  overMinutes: number;
  serviceMinutes: number;
  unapprovedMinutes: number;
  amount: number;
}

/** The ticket's time as counted (rounded per category) and the school's month. */
export interface SupportTicketTime {
  bugMinutes: number;
  supportMinutes: number;
  serviceMinutes: number;
  entries: SupportTimeEntry[];
  estimates: SupportEstimate[];
  month: SupportMonthUsage;
  hourlyRate: number;
  estimateRequested: boolean;
  approvedMinutes: number;
  loggedMinutes: number;
  estimateExhausted: boolean;
}

export type SupportAccessKeyStatus =
  'Pending' | 'Approved' | 'Rejected' | 'Revoked' | 'Expired';

/** The support's key for entering the school for a ticket: the backoffice
 * keeps its private part, the school approves the public one and may
 * revoke it any time (as SSH's authorized_keys). */
export interface SupportAccessKey {
  id: number;
  status: SupportAccessKeyStatus;
  /** Of the public key, `SHA256:…`. */
  fingerprint: string;
  operatorName: string;
  reason: string | null;
  createdAt: string;
  /** Whom the support asked to be; empty: the school chooses. */
  requestedAs: SupportAsUserRef[];
  decidedAt: string | null;
  decidedByName: string | null;
  approvedUntil: string | null;
  /** Whom the school lets the support be; empty: its admin. */
  approvedAs: SupportAsUserRef[];
  decisionNote: string | null;
  /** Approved and not run out: the support may enter. */
  usable: boolean;
  /** The caller is the school's admin (not the support inside). */
  canDecide: boolean;
  supportAs: SupportAsScope;
  /** The approval's default length. */
  approvalMinutes: number;
}

export interface SupportAsUserRef {
  id: number;
  name: string;
}

export interface SupportTicketDetail {
  ticket: SupportTicket;
  entries: SupportTicketEntry[];
  time: SupportTicketTime;
  /** The support's newest key for the ticket. */
  access: SupportAccessKey | null;
}

export const formatSupportMinutes = formatTicketMinutes;

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
