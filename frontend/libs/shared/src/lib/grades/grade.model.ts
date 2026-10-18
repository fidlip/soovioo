export interface Grade {
  id: number;
  studentId: number;
  studentName: string;
  subjectId: number;
  subjectName: string;
  value: number;
  weight: number;
  note?: string;
  /** DateOnly "yyyy-MM-dd" — when the grade was given (teacher's choice). */
  gradedOn: string;
  createdAt: string;
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
