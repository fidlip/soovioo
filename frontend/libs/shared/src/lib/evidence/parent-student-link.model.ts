export interface ParentStudentLink {
  userId: number;
  username: string;
  parentName: string;
  studentId: number;
  studentName: string;
}

export interface CreateParentStudentLinkRequest {
  userId: number;
  studentId: number;
}
