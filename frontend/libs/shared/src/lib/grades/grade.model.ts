export interface Grade {
  id: number;
  studentName: string;
  subject: string;
  value: number;
  note?: string;
  createdAt: string;
}

export interface CreateGradeRequest {
  studentName: string;
  subject: string;
  value: number;
  note?: string;
}
