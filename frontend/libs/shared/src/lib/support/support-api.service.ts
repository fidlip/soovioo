import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { LoginResponse } from '../auth/login-response.model';
import {
  CreateSuperAdminRequest,
  SchoolSupport,
  SupportAccess,
  SupportAsScope,
  SupportAsUser,
  SupportPlan,
  SupportResetPreview,
  SupportMonthUsage,
  SupportTicketDetail,
  SupportTicketSummary,
  SupportTimeCategory,
  SuperAdmin,
  UpdateSuperAdminRequest,
} from './support.model';

/** SuperAdmin support mode and SuperAdmin accounts — see SupportController;
 * the school's support tickets (kept in the backoffice) —
 * SupportTicketsController; the school's policy — SupportPolicyController. */
@Injectable({ providedIn: 'root' })
export class SupportApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  // ---- The school's tickets, kept in the backoffice ----

  getSchoolTickets(): Observable<SupportTicketSummary[]> {
    return this.http.get<SupportTicketSummary[]>(
      `${this.baseUrl}/api/my-school/support-tickets`,
    );
  }

  getSchoolPlan(): Observable<SupportPlan> {
    return this.http.get<SupportPlan>(
      `${this.baseUrl}/api/my-school/support-plan`,
    );
  }

  /** The school's support time in a month (`yyyy-MM`, none = this one). */
  getSchoolUsage(month?: string): Observable<SupportMonthUsage> {
    return this.http.get<SupportMonthUsage>(
      `${this.baseUrl}/api/my-school/support-usage`,
      { params: month ? { month } : {} },
    );
  }

  getSchoolTicket(id: number): Observable<SupportTicketDetail> {
    return this.http.get<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${id}`,
    );
  }

  /** `estimateRequested`: the support sends an estimate before any work. */
  createTicket(
    title: string,
    description: string,
    estimateRequested = false,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets`,
      { title, description, estimateRequested },
    );
  }

  addSchoolTicketMessage(
    id: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'messages', { text });
  }

  /** A message of the caller's side changed. */
  editTicketEntry(
    ticketId: number,
    entryId: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.http.put<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${ticketId}/entries/${entryId}`,
      { text },
    );
  }

  deleteTicketEntry(
    ticketId: number,
    entryId: number,
  ): Observable<SupportTicketDetail> {
    return this.http.delete<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${ticketId}/entries/${entryId}`,
    );
  }

  /** `resetChanges`: the support's changes are reset first. */
  cancelTicket(
    id: number,
    reason: string,
    resetChanges = false,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'cancel', { reason, resetChanges });
  }

  /** Resolved → closed; the author's (or any admin's, see the API). */
  closeTicket(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'close');
  }

  /** Resolved → back to the support; `text` (required) goes to the thread. */
  returnTicket(id: number, text: string): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'return', { text });
  }

  /** Closed or cancelled → as it was, while `reopenableUntil` lasts. */
  reopenTicket(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'reopen');
  }

  /** The support sends an estimate before any work. */
  requestEstimate(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'request-estimate');
  }

  /** The school's admin decides the support's estimate. */
  decideEstimate(
    id: number,
    estimateId: number,
    approve: boolean,
    note: string,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(
      id,
      `estimates/${estimateId}/${approve ? 'approve' : 'reject'}`,
      { note: note || null },
    );
  }

  /** The school disagrees with the category of logged time. */
  disputeTime(
    id: number,
    timeId: number,
    category: SupportTimeCategory,
    reason: string,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, `time/${timeId}/dispute`, {
      category,
      reason,
    });
  }

  /** What resetting the support's changes would do — of the ticket, or of
   * `sessionId` only. */
  previewReset(
    id: number,
    sessionId: number | null,
  ): Observable<SupportResetPreview> {
    return this.http.get<SupportResetPreview>(this.resetUrl(id), {
      params: sessionId ? { sessionId } : {},
    });
  }

  /** Running sessions of the ticket end. */
  resetChanges(
    id: number,
    sessionId: number | null,
    reason: string,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(this.resetUrl(id), {
      sessionId,
      reason,
    });
  }

  private resetUrl(id: number): string {
    return `${this.baseUrl}/api/my-school/support-tickets/${id}/reset`;
  }

  private schoolTicketAction(
    id: number,
    action: string,
    body: object = {},
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${id}/${action}`,
      body,
    );
  }

  getSchoolSupport(): Observable<SchoolSupport> {
    return this.http.get<SchoolSupport>(
      `${this.baseUrl}/api/my-school/support`,
    );
  }

  setSupportAccess(access: SupportAccess): Observable<SchoolSupport> {
    return this.http.put<SchoolSupport>(
      `${this.baseUrl}/api/my-school/support-access`,
      { access },
    );
  }

  setSupportAs(supportAs: SupportAsScope): Observable<SchoolSupport> {
    return this.http.put<SchoolSupport>(
      `${this.baseUrl}/api/my-school/support-as`,
      { scope: supportAs },
    );
  }

  /** The school's accounts, to name whom the support may be. */
  getSupportAsUsers(): Observable<SupportAsUser[]> {
    return this.http.get<SupportAsUser[]>(
      `${this.baseUrl}/api/my-school/support-as/users`,
    );
  }

  /** On the school's page: the support's entry signed with the key the
   * school approved → support-mode login (also sets the support cookie). */
  enter(
    entry: string,
    options: { withCredentials?: boolean } = {},
  ): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(
      `${this.baseUrl}/api/support-sessions/enter`,
      { entry },
      options,
    );
  }

  /** The school's admin lets the support in with its key: till when, as whom. */
  approveAccess(
    ticketId: number,
    keyId: number,
    until: string,
    userIds: number[],
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.accessUrl(ticketId, keyId)}/approve`,
      { until, userIds },
    );
  }

  rejectAccess(
    ticketId: number,
    keyId: number,
    note: string,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.accessUrl(ticketId, keyId)}/reject`,
      { note },
    );
  }

  /** The key stops working; the support inside leaves at once. */
  revokeAccess(
    ticketId: number,
    keyId: number,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.accessUrl(ticketId, keyId)}/revoke`,
      {},
    );
  }

  private accessUrl(ticketId: number, keyId: number): string {
    return `${this.baseUrl}/api/my-school/support-tickets/${ticketId}/access/${keyId}`;
  }

  /** Inside the school: whom the SuperAdmin may switch to. */
  getSessionUsers(): Observable<SupportAsUser[]> {
    return this.http.get<SupportAsUser[]>(
      `${this.baseUrl}/api/support-sessions/current/users`,
    );
  }

  /** Inside the school: be another user, another of their roles, or (no
   * user) the admin again — a new token of the same session. Use
   * `AuthApiService.switchSupportAs`, which stores it. */
  switchAs(as: {
    userId?: number | null;
    roleId?: number | null;
  }): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(
      `${this.baseUrl}/api/support-sessions/current/as`,
      as,
    );
  }

  /** Sent with the support token itself, with what was done if written;
   * also drops the support cookie. */
  /** Whether support mode still runs — a 401 when it was ended. */
  checkCurrentSession(): Observable<void> {
    return this.http.get<void>(`${this.baseUrl}/api/support-sessions/current`);
  }

  endCurrentSession(
    result?: string,
    options: { withCredentials?: boolean } = {},
  ): Observable<void> {
    return this.http.post<void>(
      `${this.baseUrl}/api/support-sessions/current/end`,
      { result: result ?? null },
      options,
    );
  }

  getSuperAdmins(): Observable<SuperAdmin[]> {
    return this.http.get<SuperAdmin[]>(`${this.baseUrl}/api/superadmins`);
  }

  addSuperAdmin(request: CreateSuperAdminRequest): Observable<SuperAdmin> {
    return this.http.post<SuperAdmin>(
      `${this.baseUrl}/api/superadmins`,
      request,
    );
  }

  updateSuperAdmin(
    id: number,
    request: UpdateSuperAdminRequest,
  ): Observable<SuperAdmin> {
    return this.http.put<SuperAdmin>(
      `${this.baseUrl}/api/superadmins/${id}`,
      request,
    );
  }

  /** Rejected for yourself and for the last SuperAdmin. */
  deleteSuperAdmin(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/superadmins/${id}`);
  }
}
