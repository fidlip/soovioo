import { HttpClient, HttpParams, HttpResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { MatrikaExportParams } from './matrika-export.model';

/**
 * See Soovioo.Api.Matrika.MatrikaExportService for exactly what's implemented —
 * this is a pre-pilot skeleton, not a submission-ready export.
 */
@Injectable({ providedIn: 'root' })
export class MatrikaApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /**
   * Returns the full response (not just the body) so the caller can read the
   * real filename (Z&lt;IZO&gt;_&lt;CAST&gt;.xml) from Content-Disposition
   * instead of having to reconstruct it client-side.
   */
  exportBaseFile(params: MatrikaExportParams): Observable<HttpResponse<Blob>> {
    let httpParams = new HttpParams()
      .set('cast', params.cast)
      .set('contactName', params.contactName)
      .set('contactPhone', params.contactPhone)
      .set('contactEmail', params.contactEmail);
    if (params.rdat) {
      httpParams = httpParams.set('rdat', params.rdat);
    }

    return this.http.get(`${this.baseUrl}/api/matrika/export`, {
      params: httpParams,
      responseType: 'blob',
      observe: 'response',
    });
  }
}
