import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  ClassBookClass,
  ClassBookDay,
  ClassBookLesson,
  SaveLessonRequest,
} from './class-book.model';

@Injectable({ providedIn: 'root' })
export class ClassBookApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private url(path: string): string {
    return `${this.baseUrl}/api/class-book/${path}`;
  }

  getClasses(): Observable<ClassBookClass[]> {
    return this.http.get<ClassBookClass[]>(this.url('classes'));
  }

  /** The class's lessons on the day; without a class the caller's own. */
  getDay(date: string, classId: number | null): Observable<ClassBookDay> {
    let params = new HttpParams().set('date', date);
    if (classId != null) {
      params = params.set('classId', classId);
    }
    return this.http.get<ClassBookDay>(this.url('day'), { params });
  }

  saveLesson(request: SaveLessonRequest): Observable<ClassBookLesson> {
    return this.http.put<ClassBookLesson>(this.url('lessons'), request);
  }
}
