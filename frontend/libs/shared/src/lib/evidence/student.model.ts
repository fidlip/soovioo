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
  /** State citizenship code — matrika's own doc gives "203" as the ČR example. */
  citizenship: string | null;
  /** Trvalé bydliště — human-readable address (not yet mapped to matrika's OBECB/OKRESB codes). */
  street: string | null;
  city: string | null;
  postalCode: string | null;
  /** SP_OBVOD — whether the address falls within the school's spádový obvod. */
  catchmentArea: boolean | null;
  /** Roughly ZAHDAT — also just useful on its own. */
  enrollmentDate: string | null;
  /** Emergency/guardian contact, independent of whether a parent has a Sova account. */
  contactPhone: string | null;
  contactEmail: string | null;
}

export interface CreateStudentRequest {
  firstName: string;
  lastName: string;
  schoolClassId: number;
  rodneCislo?: string;
  dateOfBirth?: string;
  gender?: Gender;
  citizenship?: string;
  street?: string;
  city?: string;
  postalCode?: string;
  catchmentArea?: boolean;
  enrollmentDate?: string;
  contactPhone?: string;
  contactEmail?: string;
}
