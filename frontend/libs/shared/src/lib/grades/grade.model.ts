export interface Grade {
  id: number;
  studentId: number;
  studentName: string;
  subjectId: number;
  subjectName: string;
  value: number;
  note?: string;
  createdAt: string;
}

export interface CreateGradeRequest {
  studentId: number;
  subjectId: number;
  value: number;
  note?: string;
}
