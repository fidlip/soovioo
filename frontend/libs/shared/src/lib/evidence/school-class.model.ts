export interface SchoolClass {
  id: number;
  name: string;
  /** ROCNIK for matrika export — optional until an admin fills it in. */
  rocnik: number | null;
}

export interface CreateSchoolClassRequest {
  name: string;
  rocnik?: number;
}
