/** Hlavní výukový blok: periods from–to a class has lessons in every day.
 * The school's, a grade's in place of it, a class's in place of its grade's;
 * those two per timetable version. */
export interface MainBlock {
  from: number;
  to: number;
}

/** A grade's own block; to save, an end null = as the school's. */
export interface GradeMainBlock {
  grade: number;
  from: number | null;
  to: number | null;
}

/** "1.–4. hodina". */
export function mainBlockText(block: MainBlock): string {
  return block.from === block.to
    ? `${block.from}. hodina`
    : `${block.from}.–${block.to}. hodina`;
}
