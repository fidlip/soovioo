import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { TeacherWishes } from './person.model';

/** The school's default wishes of teachers (Administrace → Výuka → Nastavení). */
@Injectable({ providedIn: 'root' })
export class TeacherWishesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(): Observable<TeacherWishes> {
    return this.http.get<TeacherWishes>(`${this.baseUrl}/api/teacher-wishes`);
  }

  set(wishes: TeacherWishes): Observable<TeacherWishes> {
    return this.http.put<TeacherWishes>(
      `${this.baseUrl}/api/teacher-wishes`,
      wishes,
    );
  }
}
