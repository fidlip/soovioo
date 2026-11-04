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
}

export interface CreateSubjectRequest {
  name: string;
  abbreviation?: string;
  color?: string;
  classrooms?: { classroomId?: number; type?: ClassroomType }[];
  doubleLessons?: boolean;
}

/** The subject's classrooms in order, a type expanded to its classrooms. */
export function subjectClassroomIds(
  subject: Subject | undefined,
  classrooms: Classroom[],
): number[] {
  const ids = [...(subject?.classrooms ?? [])]
    .sort((a, b) => a.position - b.position)
    .flatMap((c) =>
      c.classroomId !== null
        ? [c.classroomId]
        : classrooms.filter((r) => r.type === c.type).map((r) => r.id),
    );
  return [...new Set(ids)];
}
