import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import { AppUser, CreateUserRequest, UpdateUserRequest } from './user.model';

@Injectable({ providedIn: 'root' })
export class UsersApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getUsers(): Observable<AppUser[]> {
    return this.http.get<AppUser[]>(`${this.baseUrl}/api/users`);
  }

  addUser(request: CreateUserRequest): Observable<AppUser> {
    return this.http.post<AppUser>(`${this.baseUrl}/api/users`, request);
  }

  updateUser(id: number, request: UpdateUserRequest): Observable<AppUser> {
    return this.http.put<AppUser>(`${this.baseUrl}/api/users/${id}`, request);
  }

  /** Rejected (400) when deleting yourself or the school's last Admin. */
  deleteUser(id: number): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/api/users/${id}`);
  }

  /** The account stops working; the data stays until the school's
   * retention period for staff runs out. Rejected (400) for yourself or
   * the school's last Admin. */
  deactivate(id: number): Observable<AppUser> {
    return this.http.post<AppUser>(
      `${this.baseUrl}/api/users/${id}/deactivate`,
      {},
    );
  }

  activate(id: number): Observable<AppUser> {
    return this.http.post<AppUser>(
      `${this.baseUrl}/api/users/${id}/activate`,
      {},
    );
  }

  /** E-mails the user a one-time link to set their own password. Rejected
   * (400) if they have no e-mail on file, or (503) if this environment has
   * no e-mail infrastructure configured. */
  sendInvite(id: number): Observable<void> {
    return this.http.post<void>(`${this.baseUrl}/api/users/${id}/invite`, {});
  }
}
