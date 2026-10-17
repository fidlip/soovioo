export interface School {
  id: number;
  name: string;
  slug: string;
  createdAt: string;
  /** Matrika export fields — optional until an admin fills them in. */
  izo: string | null;
  redIzo: string | null;
  /** Institutional address/contact/director — not sent to matrika, but used
   * on real documents (report card letterhead). */
  street: string | null;
  city: string | null;
  postalCode: string | null;
  directorName: string | null;
  email: string | null;
  phone: string | null;
  /** The account Payments' client-generated SPAYD QR codes pay into. */
  bankAccountNumber: string | null;
  studentCount: number;
  userCount: number;
}

export interface CreateSchoolRequest {
  name: string;
  slug: string;
  adminUsername: string;
  adminPassword: string;
  izo?: string;
  redIzo?: string;
}

export interface UpdateSchoolRequest {
  name: string;
  slug: string;
}

export interface SetSchoolMatrikaIdsRequest {
  izo: string | null;
  redIzo: string | null;
}

export interface SetSchoolDetailsRequest {
  street: string | null;
  city: string | null;
  postalCode: string | null;
  directorName: string | null;
  email: string | null;
  phone: string | null;
}

export interface SetSchoolBankAccountRequest {
  bankAccountNumber: string | null;
}
