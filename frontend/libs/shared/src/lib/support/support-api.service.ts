import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { LoginResponse } from '../auth/login-response.model';
import {
  CreateSuperAdminRequest,
  SchoolSupport,
  SupportAccess,
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

  /** "Vstoupit do školy" for the ticket. */
  enterForTicket(id: number): Observable<SupportSessionStart> {
    return this.http.post<SupportSessionStart>(
      `${this.baseUrl}/api/support-tickets/${id}/enter`,
      {},
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

  /** `allowAccess`: lets the support in for it right away. */
  createTicket(
    title: string,
    description: string,
    allowAccess: boolean,
  ): Observable<SupportTicketDetail> {
    return this.http.post<SupportTicketDetail>(
      `${this.baseUrl}/api/my-school/support-tickets`,
      { title, description, allowAccess },
    );
  }

  addSchoolTicketMessage(
    id: number,
    text: string,
  ): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'messages', { text });
  }

  cancelTicket(id: number, reason: string): Observable<SupportTicketDetail> {
    return this.schoolTicketAction(id, 'cancel', { reason });
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

  /** Sent with the support token itself, with what was done if written;
   * also drops the support cookie. */
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
