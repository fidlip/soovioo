/** Minimal name-only teacher record for pickers (e.g. timetable entry
 * assignment), fetched by any authenticated school member — unlike the full
 * `AppUser` from `UsersApiService`, which is Admin-only. */
export interface Teacher {
  id: number;
  username: string;
  /** Display name ("Jana Nováková", or the username). */
  name: string;
  /** Aprobace. */
  qualifications: Qualification[];
}

/** One subject of a teacher's aprobace — for ročníky `gradeFrom`–`gradeTo`,
 * an open end reaching the first or the last. */
export interface Qualification {
  subjectId: number;
  gradeFrom: number | null;
  gradeTo: number | null;
}

/** A class without a ročník is covered by any range. */
export function qualifiedFor(
  qualifications: Qualification[],
  subjectId: number,
  grade: number | null,
): boolean {
  return qualifications.some(
    (q) =>
      q.subjectId === subjectId &&
      (grade === null ||
        ((q.gradeFrom === null || q.gradeFrom <= grade) &&
          (q.gradeTo === null || q.gradeTo >= grade))),
  );
}
