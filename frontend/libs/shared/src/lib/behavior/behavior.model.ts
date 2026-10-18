export type BehaviorRecordType =
  | 'Pochvala'
  | 'Poznamka'
  | 'NapomenutiTridnihoUcitele'
  | 'DutkaTridnihoUcitele'
  | 'NapomenutiReditele'
  | 'DutkaReditele';

export type SchoolHalfYear = 'First' | 'Second';

export type BehaviorClassificationValue =
  'VelmiDobre' | 'Uspokojive' | 'Neuspokojive';

export interface BehaviorRecord {
  id: number;
  studentId: number;
  studentName: string;
  type: BehaviorRecordType;
  text: string;
  /** "YYYY-MM-DD" — DateOnly on the backend. */
  date: string;
  recordedByName: string;
  createdAt: string;
  /** Null until marked read — see BehaviorController.MarkBehaviorRecordRead. */
  readAt: string | null;
}

export interface CreateBehaviorRecordRequest {
  studentId: number;
  type: BehaviorRecordType;
  text: string;
  date: string;
}

export interface BehaviorClassification {
  studentId: number;
  studentName: string;
  schoolYearStartYear: number;
  halfYear: SchoolHalfYear;
  value: BehaviorClassificationValue;
  setByName: string;
  setAt: string;
}

export interface SetBehaviorClassificationRequest {
  studentId: number;
  schoolYearStartYear: number;
  halfYear: SchoolHalfYear;
  value: BehaviorClassificationValue;
}
