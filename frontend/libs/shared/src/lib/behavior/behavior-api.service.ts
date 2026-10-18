import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  BehaviorClassification,
  BehaviorRecord,
  CreateBehaviorRecordRequest,
  SetBehaviorClassificationRequest,
} from './behavior.model';

@Injectable({ providedIn: 'root' })
export class BehaviorApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  /** Unscoped, like GradesApiService.getGrades. */
  getBehaviorRecords(): Observable<BehaviorRecord[]> {
    return this.http.get<BehaviorRecord[]>(
      `${this.baseUrl}/api/behavior-records`,
    );
  }

  addBehaviorRecord(
    request: CreateBehaviorRecordRequest,
  ): Observable<BehaviorRecord> {
    return this.http.post<BehaviorRecord>(
      `${this.baseUrl}/api/behavior-records`,
      request,
    );
  }

  /** Rejected (403) unless the caller may record this exact type for this
   * student — see BehaviorController.CanRecordTypeAsync. */
  deleteBehaviorRecord(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/behavior-records/${id}`);
  }

  getBehaviorClassifications(): Observable<BehaviorClassification[]> {
    return this.http.get<BehaviorClassification[]>(
      `${this.baseUrl}/api/behavior-classifications`,
    );
  }

  /** Upsert — setting it again for the same (student, year, half) replaces
   * the value. Rejected (403) unless the caller may set this value for
   * this student (class teacher → VelmiDobre only; Admin → anything). */
  setClassification(
    request: SetBehaviorClassificationRequest,
  ): Observable<BehaviorClassification> {
    return this.http.put<BehaviorClassification>(
      `${this.baseUrl}/api/behavior-classifications`,
      request,
    );
  }

  /** Idempotent — marking an already-read record again is a no-op. Any
   * caller who may see the record may also mark it read, not just its
   * targeted student's own Parent/Student account. */
  markRecordRead(id: number): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/behavior-records/${id}/read`,
      {},
    );
  }
}
