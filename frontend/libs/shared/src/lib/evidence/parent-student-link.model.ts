export interface ParentStudentLink {
  userId: number;
  username: string;
  studentId: number;
  studentName: string;
}

export interface CreateParentStudentLinkRequest {
  userId: number;
  studentId: number;
}
