export interface LoginRequest {
  username: string;
  password: string;
  /** Which school to log into. Omitted only for SuperAdmin (not tied to a school). */
  schoolSlug?: string;
}

export interface LoginResponse {
  token: string;
  username: string;
  role: 'SuperAdmin' | 'Admin' | 'Teacher' | 'Parent';
  schoolName: string | null;
  schoolSlug: string | null;
}
