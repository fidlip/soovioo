import { SchoolType } from './student-term';

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
  type: SchoolType;
  studentCount: number;
  userCount: number;
  /** A "Vyzkoušet" demo (listed only with `demos=true`). */
  isDemo: boolean;
  expiresAt: string | null;
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
  /** Empty/omitted clears it. */
  izo?: string;
  redIzo?: string;
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

export interface SetSchoolTypeRequest {
  type: SchoolType;
}

export interface SetSchoolBankAccountRequest {
  bankAccountNumber: string | null;
}

/** "CZ6508000000192000145399" -> "CZ65 0800 0000 1920 0014 5399" (the
 * server stores the compact IBAN). */
export function formatIban(iban: string): string {
  return iban.replace(/\s/g, '').replace(/(.{4})(?=.)/g, '$1 ');
}

/** A Czech IBAN in the domestic form people in CZ expect
 * ("CZ6508000000192000145399" -> "19-2000145399/0800"); any other IBAN
 * stays an IBAN. */
export function formatBankAccount(iban: string): string {
  const czech = /^CZ\d{2}(\d{4})(\d{6})(\d{10})$/.exec(iban.replace(/\s/g, ''));
  if (!czech) {
    return formatIban(iban);
  }
  const [, bank, prefix, number] = czech;
  const trimmedPrefix = prefix.replace(/^0+/, '');
  const trimmedNumber = number.replace(/^0+/, '');
  return `${trimmedPrefix ? `${trimmedPrefix}-` : ''}${trimmedNumber}/${bank}`;
}
