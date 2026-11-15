import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  ConfirmReportCardGradesResult,
  ReportCard,
  ReportCardClasses,
  ReportCardGradeKey,
  ReportCardGradeKind,
  ReportCardQuery,
  ReportCardResult,
} from './report-card.model';

/** Extract = výpis z vysvědčení / the particulars for the form; Verbal =
 * vysvědčení list A (slovní hodnocení) and lists B. */
export type ReportCardPrint = 'Extract' | 'Verbal';

/** The grading period a report card is of. */
export interface PeriodKey {
  gradingPeriodId: number;
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

  /** Classes whose report cards the caller sees. */
  getClasses(): Observable<ReportCardClasses> {
    return this.http.get<ReportCardClasses>(
      `${this.baseUrl}/api/report-cards/classes`,
    );
  }

  setGrade(
    period: PeriodKey,
    key: ReportCardGradeKey,
    kind: ReportCardGradeKind,
    value: number | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/report-cards/grades`, {
      ...period,
      ...key,
      kind,
      value,
    });
  }

  /** The slovní hodnocení of a subject; empty removes it. */
  setGradeText(
    period: PeriodKey,
    key: ReportCardGradeKey,
    text: string,
  ): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/report-cards/grades/text`, {
      ...period,
      ...key,
      text,
    });
  }

  setBehaviorText(
    period: PeriodKey,
    studentId: number,
    text: string,
  ): Observable<void> {
    return this.http.put<void>(
      `${this.baseUrl}/api/report-cards/behavior-text`,
      { ...period, studentId, text },
    );
  }

  /** The souhrnné hodnocení; an empty text removes it. */
  setSummary(
    period: PeriodKey,
    studentId: number,
    text: string,
    result: ReportCardResult | null,
  ): Observable<void> {
    return this.http.put<void>(`${this.baseUrl}/api/report-cards/summary`, {
      ...period,
      studentId,
      text,
      result,
    });
  }

  confirmSummary(period: PeriodKey, studentId: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/report-cards/summary/confirm`,
      { ...period, studentId },
    );
  }

  reopenSummary(period: PeriodKey, studentId: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/report-cards/summary/reopen`,
      { ...period, studentId },
    );
  }

  /** Back to the suggestion from the average. */
  resetGrade(period: PeriodKey, key: ReportCardGradeKey): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/report-cards/grades`, {
      params: params({ ...period, ...key }),
    });
  }

  confirmGrades(
    period: PeriodKey,
    grades: ReportCardGradeKey[],
  ): Observable<ConfirmReportCardGradesResult> {
    return this.http.post<ConfirmReportCardGradesResult>(
      `${this.baseUrl}/api/report-cards/grades/confirm`,
      { ...period, grades },
    );
  }

  reopenGrade(period: PeriodKey, key: ReportCardGradeKey): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/report-cards/grades/reopen`,
      { ...period, ...key },
    );
  }

  /** One pupil's report card, or the class's — a page each. */
  downloadPdf(
    period: PeriodKey,
    of: { studentId: number } | { schoolClassId: number },
    form: ReportCardPrint = 'Extract',
  ): Observable<Blob> {
    return this.http.get(`${this.baseUrl}/api/report-cards/pdf`, {
      params: params({ ...period, ...of, form }),
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
