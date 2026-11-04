export interface SchoolClass {
  id: number;
  name: string;
  /** ROCNIK for matrika export — optional until an admin fills it in. */
  rocnik: number | null;
  /** Třídní učitel — user id, nullable until assigned. */
  classTeacherId: number | null;
  /** JAZYK_O for matrika — free text (e.g. "čeština"); null defaults to Czech in practice. */
  teachingLanguage: string | null;
  /** Kmenová učebna — classroom id. */
  homeroomId: number | null;
}

export interface CreateSchoolClassRequest {
  name: string;
  rocnik?: number;
  classTeacherId?: number;
  teachingLanguage?: string;
  homeroomId?: number;
}
