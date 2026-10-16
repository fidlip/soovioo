/** Named SchoolEvent, not Event — "Event" would shadow the DOM's global
 * Event type in every file that imports it. */
export interface SchoolEvent {
  id: number;
  schoolClassId: number | null;
  title: string;
  description?: string;
  location?: string;
  startAt: string;
  endAt: string;
  allDay: boolean;
  /** "#rrggbb", or undefined for pre-existing rows created before this field. */
  color?: string;
}

export interface CreateEventRequest {
  title: string;
  description?: string;
  location?: string;
  startAt: string;
  endAt: string;
  allDay: boolean;
  schoolClassId?: number | null;
  color?: string;
}

export interface CalendarFeedUrl {
  feedUrl: string;
}
