export interface Classroom {
  id: number;
  name: string;
  /** Free text, e.g. "kmenová", "odborná — fyzika", "tělocvična". */
  type: string | null;
  capacity: number | null;
}

export interface CreateClassroomRequest {
  name: string;
  type?: string;
  capacity?: number;
}
