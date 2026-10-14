export interface Student {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassId: number;
  schoolClassName: string;
}

export interface CreateStudentRequest {
  firstName: string;
  lastName: string;
  schoolClassId: number;
}
