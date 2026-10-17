export interface Subject {
  id: number;
  name: string;
  /** Short form for timetables/report cards, e.g. "Čj". */
  abbreviation: string | null;
  /** "#rrggbb" — background color for this subject's cells on the Rozvrh table. */
  color: string | null;
}

export interface CreateSubjectRequest {
  name: string;
  abbreviation?: string;
  color?: string;
}
