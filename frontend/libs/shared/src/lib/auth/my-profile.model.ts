export interface MyProfile {
  username: string;
  role: string;
  phone: string | null;
  email: string | null;
}

export interface UpdateMyProfileRequest {
  phone?: string;
  email?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}
