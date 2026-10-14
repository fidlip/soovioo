export type AbsenceStatus = 'Unexcused' | 'PendingExcuse' | 'Excused' | 'Rejected';

export interface Absence {
  id: number;
  studentId: number;
  studentName: string;
  date: string;
  status: AbsenceStatus;
  excuseReason?: string;
}

export interface CreateAbsenceRequest {
  studentId: number;
  date: string;
}
