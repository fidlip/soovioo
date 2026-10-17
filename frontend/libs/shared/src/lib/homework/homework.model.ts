export interface HomeworkAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface Homework {
  id: number;
  schoolClassId: number;
  schoolClassName: string;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  text: string;
  /** "YYYY-MM-DD" — DateOnly on the backend. */
  dueDate: string;
  requiresCompletion: boolean;
  createdByName: string;
  createdAt: string;
  /** Empty means "assigned to the whole class". */
  targetStudentIds: number[];
  attachments: HomeworkAttachment[];
  /** Which of the (caller-visible) target students have marked it done. */
  completedStudentIds: number[];
}

export interface CreateHomeworkRequest {
  schoolClassId: number;
  subjectId: number;
  text: string;
  dueDate: string;
  requiresCompletion: boolean;
  studentIds?: number[];
}
