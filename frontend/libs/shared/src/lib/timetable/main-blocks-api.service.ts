import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { MainBlock, MainBlocks } from './main-block.model';

/** Hlavní výukový blok of the school, grades and classes (Výuka → Nastavení). */
@Injectable({ providedIn: 'root' })
export class MainBlocksApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(): Observable<MainBlocks> {
    return this.http.get<MainBlocks>(`${this.baseUrl}/api/main-blocks`);
  }

  setSchool(block: MainBlock): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/main-blocks/school`, block);
  }

  /** `null` = back to the school's. */
  setGrade(grade: number, block: MainBlock | null): Observable<void> {
    const url = `${this.baseUrl}/api/main-blocks/grades/${grade}`;
    return block
      ? this.http.put<void>(url, block)
      : this.http.delete<void>(url);
  }

  /** `null` = back to its grade's. */
  setClass(classId: number, block: MainBlock | null): Observable<void> {
    const url = `${this.baseUrl}/api/main-blocks/classes/${classId}`;
    return block
      ? this.http.put<void>(url, block)
      : this.http.delete<void>(url);
  }
}
