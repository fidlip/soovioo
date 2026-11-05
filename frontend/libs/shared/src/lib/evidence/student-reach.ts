/** Whom (and in which subjects) a permission of the caller reaches — what
 * the dialogs of Známky, Docházka and Úkoly offer. */
export interface StudentReach {
  /** Every pupil, any subject. */
  all: boolean;
  students: StudentReachRow[];
}

export interface StudentReachRow {
  studentId: number;
  /** null = any subject. */
  subjectIds: number[] | null;
  /** The caller's groups (they teach) the pupil is in. */
  studentGroupIds: number[];
}

/** Whether the pupil is reached — in `subjectId` when given. */
export function reachesStudent(
  reach: StudentReach | null,
  studentId: number,
  subjectId: number | null = null,
): boolean {
  if (!reach || reach.all) {
    return true;
  }
  const row = reach.students.find((r) => r.studentId === studentId);
  return (
    !!row &&
    (subjectId === null ||
      row.subjectIds === null ||
      row.subjectIds.includes(subjectId))
  );
}

/** The subjects reached for some pupil; null = any. */
export function reachedSubjectIds(
  reach: StudentReach | null,
): Set<number> | null {
  if (
    !reach ||
    reach.all ||
    reach.students.some((r) => r.subjectIds === null)
  ) {
    return null;
  }
  return new Set(reach.students.flatMap((r) => r.subjectIds ?? []));
}
