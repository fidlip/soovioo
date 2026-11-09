import { NameLabel } from '../people/name-label.model';
/** Named SchoolEvent, not Event — "Event" would shadow the DOM's global
 * Event type in every file that imports it. */
export interface SchoolEvent {
  id: number;
  title: string;
  description?: string;
  location?: string;
  startAt: string;
  endAt: string;
  allDay: boolean;
  /** "#rrggbb", or undefined for pre-existing rows created before this field. */
  color?: string;
  audience: EventAudience;
  /** Only meaningful when audience is "Selected" — ids of the recipient
   * rows, so the edit dialog can re-populate its pickers without a
   * separate lookup call. */
  schoolClassIds: number[];
  teacherUserIds: number[];
  parentUserIds: number[];
  studentIds: number[];
}

/** "OnlyMe": only the creator. "Everyone": everyone in the school (today's
 * default behavior). "Selected": the union of whatever recipient ids are set. */
export type EventAudience = 'OnlyMe' | 'Everyone' | 'Selected';

export interface CreateEventRequest {
  title: string;
  description?: string;
  location?: string;
  startAt: string;
  endAt: string;
  allDay: boolean;
  audience: EventAudience;
  color?: string;
  schoolClassIds?: number[];
  teacherUserIds?: number[];
  parentUserIds?: number[];
  studentIds?: number[];
}

export interface CalendarFeedUrl {
  feedUrl: string;
}

export interface SchoolClassOption {
  id: number;
  name: string;
  rocnik?: number;
}

export interface PersonOption {
  id: number;
  username: string;
  name: string;
}

export interface StudentOption {
  id: number;
  firstName: string;
  lastName: string;
  schoolClassId: number;
  schoolClassName: string;
  rocnik?: number;
  label?: NameLabel | null;
}

/** What the current caller (Teacher/Admin) may pick as event recipients —
 * scoped server-side per role, see EventsController.GetRecipientOptions.
 * `teachers` comes back empty for a Teacher caller (they can't target other
 * teachers), not omitted, so the frontend can treat it uniformly. */
export interface RecipientOptions {
  classes: SchoolClassOption[];
  teachers: PersonOption[];
  parents: PersonOption[];
  students: StudentOption[];
}
