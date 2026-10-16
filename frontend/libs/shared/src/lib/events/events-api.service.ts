import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { CalendarFeedUrl, CreateEventRequest, SchoolEvent } from './event.model';

@Injectable({ providedIn: 'root' })
export class EventsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getEvents(from?: Date, to?: Date, schoolClassId?: number): Observable<SchoolEvent[]> {
    const params: Record<string, string> = {};
    if (from) {
      params['from'] = from.toISOString();
    }
    if (to) {
      params['to'] = to.toISOString();
    }
    if (schoolClassId) {
      params['schoolClassId'] = schoolClassId.toString();
    }
    return this.http.get<SchoolEvent[]>(`${this.baseUrl}/api/events`, { params });
  }

  createEvent(request: CreateEventRequest): Observable<SchoolEvent> {
    return this.http.post<SchoolEvent>(`${this.baseUrl}/api/events`, request);
  }

  updateEvent(id: number, request: CreateEventRequest): Observable<SchoolEvent> {
    return this.http.put<SchoolEvent>(`${this.baseUrl}/api/events/${id}`, request);
  }

  deleteEvent(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/events/${id}`);
  }

  /** Lazily creates the caller's calendar-feed token on first call. */
  getCalendarFeedUrl(): Observable<CalendarFeedUrl> {
    return this.http.get<CalendarFeedUrl>(`${this.baseUrl}/api/users/me/calendar-feed-url`);
  }

  /** Invalidates the previous feed URL (e.g. after it leaked). */
  regenerateCalendarFeedUrl(): Observable<CalendarFeedUrl> {
    return this.http.post<CalendarFeedUrl>(`${this.baseUrl}/api/users/me/calendar-feed-url/regenerate`, {});
  }
}
