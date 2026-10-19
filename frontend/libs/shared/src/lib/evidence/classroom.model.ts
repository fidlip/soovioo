export type ClassroomType =
  | 'Homeroom'
  | 'Specialized'
  | 'Laboratory'
  | 'Computer'
  | 'Gym'
  | 'Workshop'
  | 'Other';

export const CLASSROOM_TYPE_LABELS: Record<ClassroomType, string> = {
  Homeroom: 'Kmenová',
  Specialized: 'Odborná',
  Laboratory: 'Laboratoř',
  Computer: 'Počítačová',
  Gym: 'Tělocvična',
  Workshop: 'Dílna',
  Other: 'Jiná',
};

export interface Classroom {
  id: number;
  name: string;
  /** Short label for timetable cells, e.g. "Tv" — null = use `name`. */
  shortName: string | null;
  type: ClassroomType | null;
  capacity: number | null;
}

export interface CreateClassroomRequest {
  name: string;
  shortName?: string;
  type?: ClassroomType;
  capacity?: number;
}
