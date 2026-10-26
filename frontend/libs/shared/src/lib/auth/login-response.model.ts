export interface LoginRequest {
  username: string;
  password: string;
  /** Which school to log into. Omitted only for SuperAdmin (not tied to a school). */
  schoolSlug?: string;
  /** "Zůstat přihlášen" — 30-day session instead of one that ends with the
   * browser (web only; the mobile app always stays signed in). */
  rememberMe?: boolean;
}

export interface LoginResponse {
  token: string;
  username: string;
  /** Kind of the active role, or SuperAdmin. */
  role: 'SuperAdmin' | 'Staff' | 'Parent' | 'Student';
  schoolName: string | null;
  schoolSlug: string | null;
  /** Mobile only — null when it was set as an httpOnly cookie (web). */
  refreshToken?: string | null;
}
