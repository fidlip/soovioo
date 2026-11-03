import { ClassroomType } from './classroom.model';

export interface Subject {
  id: number;
  name: string;
  /** Short form for timetables/report cards, e.g. "Čj". */
  abbreviation: string | null;
  /** "#rrggbb" — background color for this subject's cells on the Rozvrh table. */
  color: string | null;
  /** Pre-fills a new timetable entry's classroom when this subject is picked. */
  defaultClassroomId: number | null;
  /** Without a default classroom, the generator picks a free one of this type. */
  classroomType: ClassroomType | null;
}

export interface CreateSubjectRequest {
  name: string;
  abbreviation?: string;
  color?: string;
  defaultClassroomId?: number;
  classroomType?: ClassroomType;
}
