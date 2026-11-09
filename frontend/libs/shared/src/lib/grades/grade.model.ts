import { NameLabel } from '../people/name-label.model';
export interface Grade {
  id: number;
  studentId: number;
  studentName: string;
  /** How the caller sees the pupil when not by `studentName` — see `NameLabel`. */
  studentLabel?: NameLabel | null;
  subjectId: number;
  subjectName: string;
  value: number;
  weight: number;
  note?: string;
  /** DateOnly "yyyy-MM-dd" — when the grade was given (teacher's choice). */
  gradedOn: string;
  createdAt: string;
}

export interface GradesQuery {
  studentId?: number | null;
  subjectId?: number | null;
  page?: number;
  pageSize?: number;
}

export interface CreateGradeRequest {
  studentId: number;
  subjectId: number;
  value: number;
  weight: number;
  note?: string;
  /** DateOnly "yyyy-MM-dd"; omitted = today (create) / unchanged (update). */
  gradedOn?: string;
}
