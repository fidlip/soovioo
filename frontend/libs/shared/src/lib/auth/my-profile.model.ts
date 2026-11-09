export interface MyProfile {
  id: number;
  username: string;
  /** Display name ("Jana Nováková", or the username). */
  name: string;
  role: string;
  phone: string | null;
  email: string | null;
  /** Token of the own photo's URL, null = no photo. */
  photo: string | null;
  /** Whether the account may have a photo at all (staff; parents where the
   * school has parents' photos on). */
  canUploadPhoto: boolean;
  /** Parents and pupils have a nickname. */
  canSetNickname: boolean;
  nickname: string | null;
  /** A pupil's own, waiting for approval. */
  nicknameProposal: string | null;
  /** Whether pupils' nicknames show to the caller; null = not their choice
   * (pupils, the school's management). */
  showNicknames: boolean | null;
}

export interface UpdateMyProfileRequest {
  phone?: string;
  email?: string;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
}

export interface ChangePasswordResponse {
  token: string;
  /** Mobile only — the password change starts a new session. */
  refreshToken?: string | null;
}
