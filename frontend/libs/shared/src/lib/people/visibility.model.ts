/** What a school may share with parents and pupils ("Viditelnost údajů"),
 * see backend `People/Visibility.cs`. */
export type VisibilityOption =
  | 'StaffPhotos'
  | 'StudentPhotos'
  | 'ParentPhotos'
  | 'TeacherContacts'
  | 'Classmates'
  | 'ParentContacts';

export interface VisibilitySettings {
  shared: VisibilityOption[];
  /** Those of `shared` individuals may hide for themselves. */
  optOutAllowed: VisibilityOption[];
}

/** What the caller may hide of their own data — offered only where the
 * school shares it and leaves the choice to individuals. */
export interface Privacy {
  options: PrivacyOption[];
  /** Pupils whose entry in the class list the caller decides (own record,
   * or children). */
  students: StudentPrivacy[];
}

export interface PrivacyOption {
  option: VisibilityOption;
  hidden: boolean;
}

export interface StudentPrivacy {
  studentId: number;
  name: string;
  hidden: boolean;
}
