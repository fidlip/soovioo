import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { MainBlock } from './main-block.model';

/** The school's hlavní výukový blok (Výuka → Nastavení); grades' and
 * classes' go with the učební plán, see TeachingApiService. */
@Injectable({ providedIn: 'root' })
export class MainBlockApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  get(): Observable<MainBlock> {
    return this.http.get<MainBlock>(`${this.baseUrl}/api/main-block`);
  }

  set(block: MainBlock): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/main-block`, block);
  }
}
