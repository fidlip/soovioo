export type AbsenceStatus = 'Unexcused' | 'PendingExcuse' | 'Excused' | 'Rejected';

export interface Absence {
  id: number;
  studentId: number;
  studentName: string;
  date: string;
  status: AbsenceStatus;
  excuseReason?: string;
  /** Missed lesson numbers (0 = nultá hodina), sorted; empty = whole day. */
  periods: number[];
}

export interface CreateAbsenceRequest {
  studentId: number;
  date: string;
  periods?: number[];
}
