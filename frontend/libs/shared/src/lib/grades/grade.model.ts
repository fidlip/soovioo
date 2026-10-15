export interface Grade {
  id: number;
  studentId: number;
  studentName: string;
  subjectId: number;
  subjectName: string;
  value: number;
  weight: number;
  note?: string;
  createdAt: string;
}

export interface CreateGradeRequest {
  studentId: number;
  subjectId: number;
  value: number;
  weight: number;
  note?: string;
}
