import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { Parent } from './parent.model';

@Injectable({ providedIn: 'root' })
export class ParentsApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getParents(): Observable<Parent[]> {
    return this.http.get<Parent[]>(`${this.baseUrl}/api/parents`);
  }
}
