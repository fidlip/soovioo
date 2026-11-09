import { PagedResult } from '../common/paged-result.model';
import { SchoolClassOption } from '../events/event.model';
import { NameLabel } from './name-label.model';

export type SchoolPersonKind = 'Staff' | 'Student' | 'Parent';

/** One person in Lidé → Škola — see SchoolDirectoryController. */
export interface SchoolPerson {
  kind: SchoolPersonKind;
  name: string;
  /** Staff: classes they lead and subjects; pupils: their class. */
  notes: string;
  /** Parents: their children the caller sees, "Name (class)". */
  children: string[];
  /** Null when not shared with the caller. */
  phone: string | null;
  email: string | null;
  /** Staff always; parents only with a photo. */
  userId: number | null;
  studentId: number | null;
  photo: string | null;
  /** Pupils: how the caller sees them when not by `name`. */
  label?: NameLabel | null;
}

export interface SchoolDirectory {
  people: PagedResult<SchoolPerson>;
  /** For the class filter. */
  classes: SchoolClassOption[];
  /** The caller is a parent — the list has other parents too. */
  showsParents: boolean;
}

export interface SchoolDirectoryQuery {
  search?: string;
  kind?: SchoolPersonKind | null;
  classId?: number | null;
  page: number;
  pageSize: number;
}
