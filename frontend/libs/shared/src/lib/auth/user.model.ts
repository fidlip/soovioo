export interface AppUser {
  id: number;
  username: string;
  role: string;
  phone: string | null;
  email: string | null;
  /** Set only for role Student — the one Student this account logs in as. */
  studentId: number | null;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  role: string;
  phone?: string;
  email?: string;
  /** Required when role is 'Student'. */
  studentId?: number;
}

export interface UpdateUserRequest {
  username: string;
  role: string;
  /** Omit to keep the current password. */
  password?: string;
  phone?: string;
  email?: string;
  /** Required when role is 'Student'. */
  studentId?: number;
}
