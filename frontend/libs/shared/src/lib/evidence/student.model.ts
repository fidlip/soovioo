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
  /** KSTPR — RAKO kvalifikátor státního občanství ("3"=Občan ČR, ...). Small,
   * stable codelist — kept as a plain code + hardcoded options (see
   * CITIZENSHIP_QUALIFIER_OPTIONS), not a reference table. */
  citizenshipQualifier: string | null;
  /** STPR — actual country of citizenship, RAST číselník code. */
  countryCode: string | null;
  countryName: string | null;
  /** Trvalé bydliště — matrika doesn't code street-level addresses. */
  street: string | null;
  /** OBECB — RAUJ číselník code; OKRESB is derived from this on export. */
  municipalityCode: string | null;
  municipalityName: string | null;
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
  citizenshipQualifier?: string;
  countryCode?: string;
  street?: string;
  municipalityCode?: string;
  postalCode?: string;
  catchmentArea?: boolean;
  enrollmentDate?: string;
  contactPhone?: string;
  contactEmail?: string;
}
