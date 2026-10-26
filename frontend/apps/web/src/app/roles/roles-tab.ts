import { Component, computed, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  Permission,
  PermissionInfo,
  PermissionScope,
  ROLE_KIND_LABELS,
  Role,
  RoleKind,
  RolesApiService,
  SCOPE_LABELS,
  localizeStudentTerm,
  scopesFor,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { NotifyService } from '../notify.service';

interface AgendaRows {
  agenda: string;
  rows: PermissionInfo[];
}

/** Administrace → Role: the school's roles and what each may do. The
 * built-in ones can be renamed and given other permissions, the school's
 * own also deleted; Admin always keeps the administration itself. */
@Component({
  selector: 'app-roles-tab',
  imports: [
    Button,
    Card,
    Dialog,
    FormsModule,
    InputText,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
    ...FORM_FIELDS,
  ],
  templateUrl: './roles-tab.html',
})
export class RolesTab {
  private readonly rolesApi = inject(RolesApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly notify = inject(NotifyService);

  /** Roles were added, renamed or deleted — the account dialog lists them. */
  readonly changed = output<void>();

  protected readonly roles = signal<Role[]>([]);
  private readonly catalog = signal<PermissionInfo[]>([]);

  protected readonly agendas = computed<AgendaRows[]>(() => {
    const byAgenda = new Map<string, PermissionInfo[]>();
    for (const info of this.catalog()) {
      byAgenda.set(info.agenda, [...(byAgenda.get(info.agenda) ?? []), info]);
    }
    return [...byAgenda].map(([agenda, rows]) => ({ agenda, rows }));
  });

  protected readonly kindOptions = (
    Object.keys(ROLE_KIND_LABELS) as RoleKind[]
  ).map((value) => ({ value, label: this.kindLabel(value) }));

  /** The role being edited (its matrix), or null. */
  protected readonly editing = signal<Role | null>(null);
  protected editName = '';
  protected editPermissions: Partial<Record<Permission, PermissionScope>> = {};

  protected readonly creating = signal(false);
  protected newName = '';
  protected newKind: RoleKind = 'Staff';
  protected newCopyFrom: number | null = null;

  protected readonly deleting = signal<Role | null>(null);
  protected reassignTo: number | null = null;

  constructor() {
    this.reload();
    this.rolesApi.getCatalog().subscribe({
      next: (catalog) => this.catalog.set(catalog),
      error: (err) => this.notify.actionFailed('getCatalog failed', err),
    });
  }

  private reload(): void {
    this.rolesApi.getRoles().subscribe({
      next: (roles) => this.roles.set(roles),
      error: (err) => this.notify.actionFailed('getRoles failed', err),
    });
  }

  protected kindLabel(kind: RoleKind): string {
    return localizeStudentTerm(
      ROLE_KIND_LABELS[kind],
      this.authApi.getSchoolType(),
    );
  }

  /** How many of the permissions its kind can have the role has. */
  protected summary(role: Role): string {
    const applicable = this.catalog().filter((info) =>
      this.applies(info, role.kind),
    );
    const reached = applicable.filter(
      (info) => (role.permissions[info.permission] ?? 'None') !== 'None',
    );
    const count = `${reached.length} z ${applicable.length}`;
    return role.builtIn === 'Admin' ? `Správa školy · ${count}` : count;
  }

  protected scopeOptions(info: PermissionInfo, kind: RoleKind) {
    return scopesFor(info, kind).map((value) => ({
      value,
      label: SCOPE_LABELS[value],
    }));
  }

  /** A permission this kind of role can't have at all stays out of its matrix. */
  protected applies(info: PermissionInfo, kind: RoleKind): boolean {
    return scopesFor(info, kind).length > 1;
  }

  protected agendaApplies(group: AgendaRows, kind: RoleKind): boolean {
    return group.rows.some((info) => this.applies(info, kind));
  }

  protected startEdit(role: Role): void {
    this.editName = role.name;
    this.editPermissions = { ...role.permissions };
    this.editing.set(role);
  }

  protected saveEdit(): void {
    const role = this.editing();
    if (!role) {
      return;
    }
    this.rolesApi
      .updateRole(role.id, this.editName.trim(), this.editPermissions)
      .subscribe({
        next: () => {
          this.editing.set(null);
          this.notify.success(
            'Role uložena. Lidé s touto rolí ji mají změněnou hned.',
          );
          this.reload();
          this.changed.emit();
        },
        error: (err) => this.notify.actionFailed('updateRole failed', err),
      });
  }

  protected startCreate(): void {
    this.newName = '';
    this.newKind = 'Staff';
    this.newCopyFrom = null;
    this.creating.set(true);
  }

  /** Roles of the chosen kind to start from. */
  protected copyOptions(): Role[] {
    return this.roles().filter((r) => r.kind === this.newKind);
  }

  protected create(): void {
    this.rolesApi
      .addRole(this.newName.trim(), this.newKind, this.newCopyFrom)
      .subscribe({
        next: (role) => {
          this.creating.set(false);
          this.reload();
          this.changed.emit();
          this.startEdit(role);
        },
        error: (err) => this.notify.actionFailed('addRole failed', err),
      });
  }

  protected startDelete(role: Role): void {
    this.reassignTo = null;
    this.deleting.set(role);
  }

  /** Where the role's people can go — another role of the same kind. */
  protected reassignOptions(role: Role): Role[] {
    return this.roles().filter((r) => r.kind === role.kind && r.id !== role.id);
  }

  protected confirmDelete(): void {
    const role = this.deleting();
    if (!role) {
      return;
    }
    this.rolesApi.deleteRole(role.id, this.reassignTo).subscribe({
      next: () => {
        this.deleting.set(null);
        this.notify.success(`Role ${role.name} smazána.`);
        this.reload();
        this.changed.emit();
      },
      error: (err) => this.notify.actionFailed('deleteRole failed', err),
    });
  }
}
