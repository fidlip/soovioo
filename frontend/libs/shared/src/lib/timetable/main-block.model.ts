/** Hlavní výukový blok: periods from–to a class has lessons in every day.
 * The school's, a grade's in place of it, a class's in place of its grade's. */
export interface MainBlock {
  from: number;
  to: number;
}

/** `from`/`to` null = as the school's. */
export interface GradeMainBlock {
  grade: number;
  from: number | null;
  to: number | null;
}

/** `from`/`to` null = as its grade's (or the school's). */
export interface ClassMainBlock {
  id: number;
  name: string;
  grade: number | null;
  from: number | null;
  to: number | null;
}

export interface MainBlocks extends MainBlock {
  grades: GradeMainBlock[];
  classes: ClassMainBlock[];
}
