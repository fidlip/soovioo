export interface Subject {
  id: number;
  name: string;
  /** Short form for timetables/report cards, e.g. "Čj". */
  abbreviation: string | null;
}

export interface CreateSubjectRequest {
  name: string;
  abbreviation?: string;
}
