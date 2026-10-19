import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { PagedResult } from '../common/paged-result.model';
import {
  AuditLogEntry,
  AuditLogFilterOptions,
  AuditLogQuery,
} from './audit-log.model';

/** Agenda "Logy" — a SuperAdmin reads every school's log, a school admin
 * only their own school's (enforced by the backend). */
@Injectable({ providedIn: 'root' })
export class AuditLogApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getEntries(query: AuditLogQuery): Observable<PagedResult<AuditLogEntry>> {
    return this.http.get<PagedResult<AuditLogEntry>>(
      `${this.baseUrl}/api/audit-logs`,
      { params: toParams(query) },
    );
  }

  /** Actions ("Vytvořen uživatel") occurring under the other filters in
   * `query`, optionally only those containing `search`. */
  getActions(query: AuditLogQuery, search?: string): Observable<string[]> {
    return this.http.get<string[]>(`${this.baseUrl}/api/audit-logs/actions`, {
      params: toParams({ ...query, action: null, actionSearch: null, search }),
    });
  }

  getFilterOptions(
    schoolId?: number | null,
  ): Observable<AuditLogFilterOptions> {
    return this.http.get<AuditLogFilterOptions>(
      `${this.baseUrl}/api/audit-logs/filter-options`,
      { params: toParams({ schoolId }) },
    );
  }
}

function toParams(query: object): HttpParams {
  let params = new HttpParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== null && value !== undefined && value !== '') {
      params = params.set(key, String(value));
    }
  }
  return params;
}
