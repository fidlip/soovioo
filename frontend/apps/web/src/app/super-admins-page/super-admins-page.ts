import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Password } from '@openng/optimus-ui/password';
import { TableModule } from '@openng/optimus-ui/table';
import { AuthApiService, SuperAdmin, SupportApiService } from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { FORM_FIELDS, FloatField } from '../forms';
import { NotifyService } from '../notify.service';

/** SuperAdmin's "Superadmini": the accounts that manage the schools. */
@Component({
  selector: 'app-super-admins-page',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    AppIcon,
    FormsModule,
    Button,
    Card,
    Dialog,
    InputText,
    Password,
    PrimeTemplate,
    TableModule,
  ],
  templateUrl: './super-admins-page.html',
})
export class SuperAdminsPage {
  private readonly notify = inject(NotifyService);
  private readonly supportApi = inject(SupportApiService);

  protected readonly superAdmins = signal<SuperAdmin[]>([]);
  protected readonly myUserId = inject(AuthApiService).getUserId();
  protected readonly superAdminDialogVisible = signal(false);
  /** null = the dialog adds a new SuperAdmin, otherwise edits this one. */
  protected editingSuperAdminId: number | null = null;
  protected editingBuiltIn = false;
  protected superAdminFirstName = '';
  protected superAdminLastName = '';
  protected superAdminUsername = '';
  protected superAdminPassword = '';
  protected superAdminPhone = '';
  protected superAdminEmail = '';

  constructor() {
    this.reloadSuperAdmins();
  }

  protected openAddSuperAdminDialog(): void {
    this.editingSuperAdminId = null;
    this.fillSuperAdminForm(null);
  }

  protected openEditSuperAdminDialog(superAdmin: SuperAdmin): void {
    this.editingSuperAdminId = superAdmin.id;
    this.fillSuperAdminForm(superAdmin);
  }

  protected saveSuperAdmin(): void {
    const fields = {
      username: this.superAdminUsername.trim(),
      firstName: this.superAdminFirstName.trim() || undefined,
      lastName: this.superAdminLastName.trim() || undefined,
      phone: this.superAdminPhone.trim() || undefined,
      email: this.superAdminEmail.trim() || undefined,
    };
    const editingId = this.editingSuperAdminId;
    const result =
      editingId === null
        ? this.supportApi.addSuperAdmin({
            ...fields,
            password: this.superAdminPassword,
          })
        : this.supportApi.updateSuperAdmin(editingId, {
            ...fields,
            password: this.superAdminPassword || undefined,
          });
    result.subscribe({
      next: (saved) => {
        this.notify.success(
          editingId === null
            ? `Superadmin ${saved.name} přidán — při prvním přihlášení si změní heslo.`
            : 'Uloženo.',
        );
        this.superAdminDialogVisible.set(false);
        this.reloadSuperAdmins();
      },
      error: (err) => this.notify.actionFailed('saveSuperAdmin failed', err),
    });
  }

  private fillSuperAdminForm(superAdmin: SuperAdmin | null): void {
    this.editingBuiltIn = superAdmin?.builtIn ?? false;
    this.superAdminFirstName = superAdmin?.firstName ?? '';
    this.superAdminLastName = superAdmin?.lastName ?? '';
    this.superAdminUsername = superAdmin?.username ?? '';
    this.superAdminPassword = '';
    this.superAdminPhone = superAdmin?.phone ?? '';
    this.superAdminEmail = superAdmin?.email ?? '';
    this.notify.clear();
    this.superAdminDialogVisible.set(true);
  }

  protected deleteSuperAdmin(superAdmin: SuperAdmin): void {
    this.supportApi.deleteSuperAdmin(superAdmin.id).subscribe({
      next: () => this.reloadSuperAdmins(),
      error: (err) => this.notify.actionFailed('deleteSuperAdmin failed', err),
    });
  }

  private reloadSuperAdmins(): void {
    this.supportApi.getSuperAdmins().subscribe({
      next: (list) => this.superAdmins.set(list),
      error: (err) => console.error('getSuperAdmins failed', err),
    });
  }
}
