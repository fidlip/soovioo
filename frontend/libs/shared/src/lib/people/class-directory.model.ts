import { NameLabel } from './name-label.model';
/** "Třída" for a parent or pupil — sections the school doesn't share come
 * back null, contacts it doesn't share null too. */
export interface ClassDirectory {
  studentId: number;
  studentName: string;
  className: string;
  teachers: DirectoryTeacher[];
  classmates: Classmate[] | null;
  parents: DirectoryParent[] | null;
  studentLabel?: NameLabel | null;
}

export interface DirectoryTeacher {
  userId: number;
  name: string;
  /** "třídní učitel, Matematika, …" */
  notes: string;
  phone: string | null;
  email: string | null;
  /** Photo token for `PhotosApiService` URLs — present only when the
   * caller may see the photo. */
  photo?: string | null;
}

export interface Classmate {
  studentId: number;
  name: string;
  label?: NameLabel | null;
  /** Photo token, present only when the caller may see the photo. */
  photo?: string | null;
}

export interface DirectoryParent {
  name: string;
  /** Their children in the class — empty unless the class list is shared. */
  children: string[];
  phone: string | null;
  email: string | null;
  /** Set only with `photo` — the photo URL needs it. */
  userId?: number | null;
  /** Photo token, present only when the caller may see the photo. */
  photo?: string | null;
}
