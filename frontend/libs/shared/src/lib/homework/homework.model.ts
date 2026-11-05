export interface HomeworkAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface Homework {
  id: number;
  /** null for homework of a group. */
  schoolClassId: number | null;
  /** The class, or the group as "Nj (7.A, 7.B)". */
  schoolClassName: string;
  studentGroupId: number | null;
  /** The class, or the group's classes. */
  schoolClassIds: number[];
  /** The (caller-visible) pupils of the group now; null for a class's. */
  groupStudentIds: number[] | null;
  subjectId: number;
  subjectName: string;
  subjectAbbreviation: string | null;
  text: string;
  /** "YYYY-MM-DD" — DateOnly on the backend. */
  dueDate: string;
  requiresCompletion: boolean;
  createdByName: string;
  createdAt: string;
  /** Empty means "assigned to the whole class / group". */
  targetStudentIds: number[];
  attachments: HomeworkAttachment[];
  /** Which of the (caller-visible) target students have marked it done. */
  completedStudentIds: number[];
}

export interface CreateHomeworkRequest {
  /** null with `studentGroupId`. */
  schoolClassId: number | null;
  /** Homework of a group; the subject is the group's. */
  studentGroupId?: number | null;
  subjectId: number;
  text: string;
  dueDate: string;
  requiresCompletion: boolean;
  studentIds?: number[];
}
