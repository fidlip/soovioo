export interface AppUser {
  id: number;
  username: string;
  role: string;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  role: string;
}

export interface UpdateUserRequest {
  username: string;
  role: string;
  /** Omit to keep the current password. */
  password?: string;
}
