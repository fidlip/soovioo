import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { SchoolHalfYear } from '../behavior/behavior.model';
import {
  ConfirmReportCardGradesResult,
  ReportCard,
  ReportCardGradeKey,
  ReportCardGradeKind,
  ReportCardQuery,
} from './report-card.model';

export interface HalfYearKey {
  schoolYearStartYear: number;
  halfYear: SchoolHalfYear;
}

@Injectable({ providedIn: 'root' })
export class ReportCardApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getReportCards(query: ReportCardQuery): Observable<ReportCard[]> {
    return this.http.get<ReportCard[]>(`${this.baseUrl}/api/report-cards`, {
      params: params({ ...query }),
    });
  }

  setGrade(
    halfYear: HalfYearKey,
    key: ReportCardGradeKey,
    kind: ReportCardGradeKind,
    value: number | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/report-cards/grades`, {
      ...halfYear,
      ...key,
      kind,
      value,
    });
  }

  /** Back to the suggestion from the average. */
  resetGrade(halfYear: HalfYearKey, key: ReportCardGradeKey): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/report-cards/grades`, {
      params: params({ ...halfYear, ...key }),
    });
  }

  confirmGrades(
    halfYear: HalfYearKey,
    grades: ReportCardGradeKey[],
  ): Observable<ConfirmReportCardGradesResult> {
    return this.http.post<ConfirmReportCardGradesResult>(
      `${this.baseUrl}/api/report-cards/grades/confirm`,
      { ...halfYear, grades },
    );
  }

  reopenGrade(
    halfYear: HalfYearKey,
    key: ReportCardGradeKey,
  ): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/report-cards/grades/reopen`,
      { ...halfYear, ...key },
    );
  }

  /** One pupil's report card, or the class's — a page each. */
  downloadPdf(
    halfYear: HalfYearKey,
    of: { studentId: number } | { schoolClassId: number },
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/api/report-cards/pdf`, {
      params: params({ ...halfYear, ...of }),
      responseType: 'blob',
    });
  }
}

function params(
  values: Record<string, string | number | null | undefined>,
): HttpParams {
  let result = new HttpParams();
  for (const [key, value] of Object.entries(values)) {
    if (value !== null && value !== undefined) {
      result = result.set(key, value);
    }
  }
  return result;
}
