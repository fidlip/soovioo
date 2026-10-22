export type PhotoSize = 'thumb' | 'profile';

/** A pupil's photo as the caller sees it, with what they may do — see
 * backend `PhotosController`. */
export interface StudentPhotoState {
  studentId: number;
  name: string;
  /** Token for the photo URL — only when the caller may see it. */
  photo: string | null;
  status: 'Pending' | 'Approved' | null;
  consentGivenBy: string | null;
  consentAt: string | null;
  /** The school has pupils' photos on. */
  photosEnabled: boolean;
  canUpload: boolean;
  canApprove: boolean;
  canDelete: boolean;
  canManageConsent: boolean;
  /** The caller records a consent for someone else (school side) and has
   * to name the guardian. */
  consentNeedsName: boolean;
}
