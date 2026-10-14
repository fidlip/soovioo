import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';

@Injectable({ providedIn: 'root' })
export class ReportCardApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  downloadReportCard(studentId: number): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/api/students/${studentId}/report-card`, {
      responseType: 'blob',
    });
  }
}
