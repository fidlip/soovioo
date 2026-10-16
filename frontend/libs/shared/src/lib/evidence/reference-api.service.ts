import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Country, Municipality } from './reference.model';

@Injectable({ providedIn: 'root' })
export class ReferenceApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Only 252 rows — the full list, for a plain dropdown/autocomplete. */
  getCountries(): Observable<Country[]> {
    return this.http.get<Country[]>(`${this.baseUrl}/api/countries`);
  }

  /** 6400+ rows — never fetched in full. Returns at most 20 matches; the
   * backend ignores queries under 2 characters. */
  searchMunicipalities(query: string): Observable<Municipality[]> {
    return this.http.get<Municipality[]>(`${this.baseUrl}/api/municipalities`, { params: { query } });
  }
}
