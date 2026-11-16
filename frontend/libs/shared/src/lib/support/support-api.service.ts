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
  SupportTicket,
  SupportTicketDetail,
  SupportTicketFilter,
  SupportTicketScope,
  SupportTicketStatus,
  SuperAdmin,
  SupportSession,
  SupportSessionStart,
  UpdateSuperAdminRequest,
} from './support.model';

/** SuperAdmin support mode and SuperAdmin accounts — see SupportController;
 * support tickets — SupportTicketsController; the school's policy —
 * SupportPolicyController. */
@Injectable({ providedIn: 'root' })
export class SupportApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  // ---- SuperAdmin: tickets ----

  getTickets(
    filter: SupportTicketFilter = 'Open',
    schoolId?: number | null,
  ): Observable<SupportTicket[]> {
    return this.http.get<SupportTicket[]>(
      `${this.baseUrl}/api/support-tickets`,
      { params: schoolId ? { filter, schoolId } : { filter } },
    );
  }

  getTicket(id: number): Observable<SupportTicketDetail> {
    return this.http.get<SupportTicketDetail>(
      `${this.baseUrl}/api/support-tickets/${id}`,
    );
  }

  /** Opened for a school — the caller takes care of it. */
  createTicketForSchool(
    schoolId: number,
    title: string,
    description: string,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/support-tickets`,
      { schoolId, title, description },
    );
  }

  /** "Převzít", or with `userId` of another SuperAdmin "Předat". */
  assignTicket(id: number, userId?: number): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'assign', userId ? { userId } : {});
  }

  /** "Převzít od" — from the assignee the caller sees (`fromUserId`). */
  takeOverTicket(
    id: number,
    fromUserId: number,
  ): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'assign', { takeOverFrom: fromUserId });
  }

  /** A message of the support made an internal note, or a note a message
   * to the school (which hears of it). */
  setTicketEntryInternal(
    id: number,
    entryId: number,
    internal: boolean,
  ): Observable<SupportTicketDetail> {
    return this.ticketAction(id, `entries/${entryId}/internal`, { internal });
  }

  /** A message or note of the caller's side changed — `ticketId` is
   * ignored in support mode (the visit's ticket). */
  editTicketEntry(
    scope: SupportTicketScope,
    ticketId: number,
    entryId: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.http.put<SupportTicketDetail>(
      this.entryUrl(scope, ticketId, entryId),
      { text },
    );
  }

  deleteTicketEntry(
    scope: SupportTicketScope,
    ticketId: number,
    entryId: number,
  ): Observable<SupportTicketDetail> {
    return this.http.delete<SupportTicketDetail>(
      this.entryUrl(scope, ticketId, entryId),
    );
  }

  private entryUrl(
    scope: SupportTicketScope,
    ticketId: number,
    entryId: number,
  ): string {
    const ticket = {
      support: `api/support-tickets/${ticketId}`,
      session: 'api/support-sessions/current/ticket',
      school: `api/my-school/support-tickets/${ticketId}`,
    }[scope];
    return `${this.baseUrl}/${ticket}/entries/${entryId}`;
  }

  /** "Přerušit práci" — back to the pool with a summary. */
  releaseTicket(id: number, summary: string): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'release', { summary });
  }

  setTicketStatus(
    id: number,
    status: SupportTicketStatus,
  ): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'status', { status });
  }

  /** `internal`: a note only the SuperAdmins see. */
  addTicketMessage(
    id: number,
    text: string,
    internal: boolean,
  ): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'messages', { text, internal });
  }

  requestTicketAccess(id: number): Observable<SupportTicketDetail> {
    return this.ticketAction(id, 'request-access');
  }

  /** "Vstoupit do školy" for the ticket — as the school's admin, or as
   * `as.userId` (in `as.roleId`, else their first allowed role). */
  enterForTicket(
    id: number,
    as: { userId: number; roleId?: number } | null = null,
  ): Observable<SupportSessionStart> {
    return this.http.post<SupportSessionStart>(
      `${this.baseUrl}/api/support-tickets/${id}/enter`,
      as ?? {},
    );
  }

  /** Whom the assignee may enter as (the school's `Users`), the author first. */
  getEnterAs(id: number): Observable<SupportAsUser[]> {
    return this.http.get<SupportAsUser[]>(
      `${this.baseUrl}/api/support-tickets/${id}/enter-as`,
    );
  }

  private ticketAction(
    id: number,
    action: string,
    body: object = {},
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/support-tickets/${id}/${action}`,
      body,
    );
  }

  // ---- The school's admin ----

  getSchoolTickets(): Observable<SupportTicket[]> {
    return this.http.get<SupportTicket[]>(
      `${this.baseUrl}/api/my-school/support-tickets`,
    );
  }

  getSchoolPlan(): Observable<SupportPlan> {
    return this.http.get<SupportPlan>(
      `${this.baseUrl}/api/my-school/support-plan`,
    );
  }

  /** In support mode: the ticket the visit is for, its whole thread included. */
  getSessionTicket(): Observable<SupportTicketDetail> {
    return this.http.get<SupportTicketDetail>(
      `${this.baseUrl}/api/support-sessions/current/ticket`,
    );
  }

  /** In support mode: a message to the school or an internal note. */
  addSessionTicketMessage(
    text: string,
    internal: boolean,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/support-sessions/current/ticket/messages`,
      { text, internal },
    );
  }

  /** In support mode: a message made an internal note, or back. */
  setSessionEntryInternal(
    entryId: number,
    internal: boolean,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/support-sessions/current/ticket/entries/${entryId}/internal`,
      { internal },
    );
  }

  getSchoolTicket(id: number): Observable<SupportTicketDetail> {
    return this.http.get<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${id}`,
    );
  }

  /** `allowAccess`: lets the support in for it right away;
   * `supportAsUserIds`: whom it may enter as (the school's `Users`). */
  createTicket(
    title: string,
    description: string,
    allowAccess: boolean,
    supportAsUserIds?: number[],
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets`,
      { title, description, allowAccess, supportAsUserIds },
    );
  }

  addSchoolTicketMessage(
    id: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'messages', { text });
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

  /** Resolved → back to its assignee; `text` (required) goes to the thread. */
  returnTicket(id: number, text: string): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'return', { text });
  }

  /** A SuperAdmin's: resolved → back to its assignee; `text` is an
   * optional internal note. */
  returnTicketBySupport(
    id: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/support-tickets/${id}/return`,
      { text: text || null },
    );
  }

  /** What resetting the support's changes would do — of the ticket, or of
   * `sessionId` only; the school's side ('school') or the assignee's. */
  previewReset(
    scope: 'support' | 'school',
    id: number,
    sessionId: number | null,
  ): Observable<SupportResetPreview> {
    return this.http.get<SupportResetPreview>(`${this.resetUrl(scope, id)}`, {
      params: sessionId ? { sessionId } : {},
    });
  }

  /** `reason`: required of the school. Running sessions of the ticket end. */
  resetChanges(
    scope: 'support' | 'school',
    id: number,
    sessionId: number | null,
    reason: string,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(this.resetUrl(scope, id), {
      sessionId,
      reason: reason || null,
    });
  }

  private resetUrl(scope: 'support' | 'school', id: number): string {
    return scope === 'school'
      ? `${this.baseUrl}/api/my-school/support-tickets/${id}/reset`
      : `${this.baseUrl}/api/support-tickets/${id}/reset`;
  }

  /** Closed or cancelled → as it was, while `reopenableUntil` lasts. */
  reopenTicket(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'reopen');
  }

  grantTicketAccess(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'grant-access');
  }

  /** Sessions entered for the ticket end at once. */
  revokeTicketAccess(id: number): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'revoke-access');
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

  /** The school's accounts, to name on a ticket whom the support may be. */
  getSupportAsUsers(): Observable<SupportAsUser[]> {
    return this.http.get<SupportAsUser[]>(
      `${this.baseUrl}/api/my-school/support-as/users`,
    );
  }

  /** The users the support may enter as for the ticket; none = only as
   * the admin. */
  setTicketSupportAsUsers(
    id: number,
    userIds: number[],
  ): Observable<SupportTicketDetail> {
    return this.http.put<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets/${id}/support-as`,
      { userIds },
    );
  }

  /** On the school's page: one-time code → support-mode login (also sets
   * the support cookie that keeps it alive across reloads). */
  redeem(
    code: string,
    options: { withCredentials?: boolean } = {},
  ): Observable<LoginResponse> {
    return this.http.post<LoginResponse>(
      `${this.baseUrl}/api/support-sessions/redeem`,
      { code },
      options,
    );
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

  getSupportSessions(): Observable<SupportSession[]> {
    return this.http.get<SupportSession[]>(
      `${this.baseUrl}/api/support-sessions`,
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
