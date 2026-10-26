import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { API_BASE_URL } from '../api-base-url.token';
import {
  Permission,
  PermissionInfo,
  PermissionScope,
  Role,
  RoleKind,
} from './role.model';

/** Administrace → Role (Admin only). */
@Injectable({ providedIn: 'root' })
export class RolesApiService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  getRoles(): Observable<Role[]> {
    return this.http.get<Role[]>(`${this.baseUrl}/api/roles`);
  }

  getCatalog(): Observable<PermissionInfo[]> {
    return this.http.get<PermissionInfo[]>(`${this.baseUrl}/api/roles/catalog`);
  }

  /** With the permissions of `copyFromRoleId` (as far as the kind allows). */
  addRole(
    name: string,
    kind: RoleKind,
    copyFromRoleId?: number | null,
  ): Observable<Role> {
    return this.http.post<Role>(`${this.baseUrl}/api/roles`, {
      name,
      kind,
      copyFromRoleId: copyFromRoleId ?? null,
    });
  }

  updateRole(
    id: number,
    name: string,
    permissions: Partial<Record<Permission, PermissionScope>>,
  ): Observable<Role> {
    return this.http.put<Role>(`${this.baseUrl}/api/roles/${id}`, {
      name,
      permissions,
    });
  }

  /** Whoever has the role moves to `reassignToRoleId` (required then). */
  deleteRole(id: number, reassignToRoleId?: number | null): Observable<void> {
    const query = reassignToRoleId
      ? `?reassignToRoleId=${reassignToRoleId}`
      : '';
    return this.http.delete<void>(`${this.baseUrl}/api/roles/${id}${query}`);
  }
}
