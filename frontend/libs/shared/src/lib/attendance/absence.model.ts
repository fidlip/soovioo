import { NameLabel } from '../people/name-label.model';
export type AbsenceStatus =
  'Unexcused' | 'PendingExcuse' | 'Excused' | 'Rejected';

export interface Absence {
  id: number;
  studentId: number;
  studentName: string;
  /** How the caller sees the pupil when not by `studentName` — see `NameLabel`. */
  studentLabel?: NameLabel | null;
  date: string;
  status: AbsenceStatus;
  /** The reason of the omluvenka the day belongs to. */
  excuseReason?: string | null;
  /** Missed lesson numbers (0 = nultá hodina), sorted; empty = whole day. */
  periods: number[];
  /** The omluvenka the day belongs to. */
  excuseId?: number | null;
  /** Why the teacher rejected the omluvenka for this day. */
  reviewNote?: string | null;
}

export interface CreateAbsenceRequest {
  studentId: number;
  date: string;
  periods?: number[];
}

/** How the family gave an omluvenka to the teacher who wrote it down. */
export type ExcuseChannel =
  | 'Phone'
  | 'InPerson'
  | 'Email'
  | 'Letter'
  | 'DataBox'
  | 'TextMessage'
  | 'Other';

export const EXCUSE_CHANNEL_LABELS: Record<ExcuseChannel, string> = {
  Phone: 'Telefonicky',
  InPerson: 'Osobně',
  Email: 'E-mailem',
  Letter: 'Písemně (omluvný list, žákovská knížka)',
  DataBox: 'Datovou schránkou',
  TextMessage: 'SMS',
  Other: 'Jinak',
};

export type ExcuseStatus =
  'Pending' | 'Approved' | 'Rejected' | 'PartlyApproved';

export interface ExcuseDay {
  absenceId: number;
  date: string;
  status: AbsenceStatus;
  reviewNote: string | null;
  periods: number[];
}

export interface ExcuseAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

/** An omluvenka: why a pupil is or was away, from one day to another. */
export interface Excuse {
  id: number;
  studentId: number;
  studentName: string;
  /** How the caller sees the pupil when not by `studentName` — see `NameLabel`. */
  studentLabel?: NameLabel | null;
  dateFrom: string;
  dateTo: string;
  /** Of a one-day omluvenka; empty = the whole day. */
  periods: number[];
  reason: string;
  /** Null when the family wrote it here themselves. */
  channel: ExcuseChannel | null;
  status: ExcuseStatus;
  days: ExcuseDay[];
  attachments: ExcuseAttachment[];
  createdByName: string | null;
  createdById: number | null;
  createdAt: string;
}

export interface ExcuseRequest {
  studentId: number;
  dateFrom: string;
  dateTo: string;
  reason: string;
  /** Only for one day; empty = the whole day. */
  periods?: number[];
  /** Required when staff write it down. */
  channel?: ExcuseChannel | null;
}

export interface ExcuseResult {
  /** Null when no day of it is left. */
  excuseId: number | null;
  /** Days left out — already excused, rejected or under another omluvenka. */
  skipped: { date: string; status: AbsenceStatus }[];
}
