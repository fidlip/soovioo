import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Person, SavePersonRequest } from './person.model';

/** Administrace → Osoby (Admin only). */
@Injectable({ providedIn: 'root' })
export class PeopleApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getPeople(): Observable<Person[]> {
    return this.http.get<Person[]>(`${this.baseUrl}/api/people`);
  }

  getPerson(id: number): Observable<Person> {
    return this.http.get<Person>(`${this.baseUrl}/api/people/${id}`);
  }

  addPerson(request: SavePersonRequest): Observable<Person> {
    return this.http.post<Person>(`${this.baseUrl}/api/people`, request);
  }

  updatePerson(id: number, request: SavePersonRequest): Observable<Person> {
    return this.http.put<Person>(`${this.baseUrl}/api/people/${id}`, request);
  }

  /** 409 for someone with an account, or a pupil with history (the text
   * says what's there). */
  deletePerson(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/people/${id}`);
  }
}
