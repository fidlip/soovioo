export type Gender = 'Male' | 'Female';

export interface Student {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassId: number;
  schoolClassName: string;
  /** Matrika export fields — optional until an admin fills them in. */
  rodneCislo: string | null;
  dateOfBirth: string | null;
  gender: Gender | null;
}

export interface CreateStudentRequest {
  firstName: string;
  lastName: string;
  schoolClassId: number;
  rodneCislo?: string;
  dateOfBirth?: string;
  gender?: Gender;
}
