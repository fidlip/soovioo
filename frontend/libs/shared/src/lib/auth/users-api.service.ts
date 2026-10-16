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
}
