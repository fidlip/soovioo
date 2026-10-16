export interface AppUser {
  id: number;
  username: string;
  role: string;
  phone: string | null;
  email: string | null;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  role: string;
  phone?: string;
  email?: string;
}

export interface UpdateUserRequest {
  username: string;
  role: string;
  /** Omit to keep the current password. */
  password?: string;
  phone?: string;
  email?: string;
}
