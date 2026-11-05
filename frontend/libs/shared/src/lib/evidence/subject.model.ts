import { Classroom, ClassroomType } from './classroom.model';

/** A particular classroom, or any of a type. */
export interface SubjectClassroom {
  position: number;
  classroomId: number | null;
  type: ClassroomType | null;
}

export interface Subject {
  id: number;
  name: string;
  /** Short form for timetables/report cards, e.g. "Čj". */
  abbreviation: string | null;
  /** "#rrggbb" — background color for this subject's cells on the Rozvrh table. */
  color: string | null;
  /** Where it's taught, first choice first (sort by position). */
  classrooms: SubjectClassroom[];
  /** In blocks of two lessons in a row; the učební plán can say otherwise. */
  doubleLessons: boolean;
  /** A row of the učební plán divisions of other subjects teach — not
   * graded or given homework itself. */
  coveredByDivisions?: boolean;
}

export interface CreateSubjectRequest {
  name: string;
  abbreviation?: string;
  color?: string;
  classrooms?: { classroomId?: number; type?: ClassroomType }[];
  doubleLessons?: boolean;
}

/** The subject's classrooms for a class in order, a type expanded to its
 * classrooms; the homeroom type and a subject without a list mean the class's
 * own homeroom. */
export function subjectClassroomIds(
  subject: Subject | undefined,
  classrooms: Classroom[],
  homeroomId: number | null,
): number[] {
  const homeroom = homeroomId === null ? [] : [homeroomId];
  if (!subject) {
    return [];
  }
  if (subject.classrooms.length === 0) {
    return homeroom;
  }
  const ids = [...subject.classrooms]
    .sort((a, b) => a.position - b.position)
    .flatMap((c) =>
      c.classroomId !== null
        ? [c.classroomId]
        : c.type === 'Homeroom'
          ? homeroom
          : classrooms.filter((r) => r.type === c.type).map((r) => r.id),
    );
  return [...new Set(ids)];
}
