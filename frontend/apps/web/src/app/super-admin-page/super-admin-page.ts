import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Password } from '@openng/optimus-ui/password';
import { TableModule } from '@openng/optimus-ui/table';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import {
  AuthApiService,
  CreateSchoolRequest,
  DateFormatPipe,
  School,
  SchoolsApiService,
  SuperAdmin,
  SupportApiService,
  UpdateSchoolRequest,
} from '@soovioo/shared';
import { schoolUrl } from '../school-context';
import { AppIcon } from '../app-icon';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS } from '../forms';
import { DemoAdminPanel } from './demo-admin-panel';

const DEFAULT_PAGE_SIZE = 10;

@Component({
  selector: 'app-super-admin-page',
  imports: [
    ...FORM_FIELDS,
    AppIcon,
    DateFormatPipe,
    FormsModule,
    Button,
    Card,
    Dialog,
    InputText,
    Paginator,
    Password,
    PrimeTemplate,
    TableModule,
    ToggleSwitch,
    DemoAdminPanel,
  ],
  templateUrl: './super-admin-page.html',
})
export class SuperAdminPage {
  protected readonly notify = inject(NotifyService);
  private readonly schoolsApi = inject(SchoolsApiService);
  private readonly supportApi = inject(SupportApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly confirmation = inject(ConfirmationService);

  protected readonly superAdmins = signal<SuperAdmin[]>([]);
  protected readonly myUserId = this.authApi.getUserId();
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

  protected readonly schools = signal<School[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  protected search = '';

  /** The list shows the "Vyzkoušet" demos instead of the real schools. */
  protected showDemos = false;
  protected readonly demoCount = signal(0);

  protected readonly schoolDialogVisible = signal(false);
  protected newSchoolName = '';
  protected newSchoolSlug = '';
  protected newAdminUsername = '';
  protected newAdminPassword = '';
  protected newSchoolIzo = '';
  protected newSchoolRedIzo = '';

  /** null = the school dialog creates a new school, otherwise edits this one. */
  protected editingSchoolId: number | null = null;

  /** A signal, not a plain field — this app has no zone.js polyfill, so a
   * plain-field write inside an RxJS subscribe callback doesn't reliably
   * schedule change detection on its own. */

  constructor() {
    this.reload();
    this.schoolsApi.getSchools(1, 1, undefined, true).subscribe({
      next: (result) => this.demoCount.set(result.totalCount),
      error: (err) => console.error('demo count failed', err),
    });
    this.reloadSuperAdmins();
  }

  /** "Vstoupit do školy": an audited, 2-hour Admin session in that school
   * (support mode); the own SuperAdmin session comes back on "Opustit". */
  protected enterSchool(school: School): void {
    this.supportApi.startSupportSession(school.id).subscribe({
      next: (session) => {
        // A full page load onto the school's page (its own subdomain in
        // production) — the same path in every deployment. The one-time
        // code rides in the fragment, which never reaches any server log.
        window.location.href = `${schoolUrl(session.schoolSlug, 'support/enter')}#code=${session.code}`;
      },
      error: (err) =>
        this.notify.actionFailed('startSupportSession failed', err),
    });
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

  protected onPageChange(event: PaginatorState): void {
    this.page.set((event.page ?? 0) + 1);
    this.pageSize.set(event.rows ?? DEFAULT_PAGE_SIZE);
    this.reload();
  }

  protected onSearchChange(): void {
    this.page.set(1);
    this.reload();
  }

  private reload(): void {
    this.schoolsApi
      .getSchools(
        this.page(),
        this.pageSize(),
        this.search.trim() || undefined,
        this.showDemos,
      )
      .subscribe({
        next: (result) => {
          // A delete can empty out the last page (e.g. the only school left on
          // page 2 gets removed) — step back one page rather than show a
          // confusing "no schools" table while schools still exist.
          if (
            result.items.length === 0 &&
            result.totalCount > 0 &&
            this.page() > 1
          ) {
            this.page.set(this.page() - 1);
            this.reload();
            return;
          }
          this.schools.set(result.items);
          this.totalCount.set(result.totalCount);
          if (this.showDemos) {
            this.demoCount.set(result.totalCount);
          }
        },
        error: (err) => console.error('getSchools failed', err),
      });
  }

  protected openEditSchoolDialog(school: School): void {
    this.openAddSchoolDialog();
    this.editingSchoolId = school.id;
    this.newSchoolName = school.name;
    this.newSchoolSlug = school.slug;
    this.newSchoolIzo = school.izo ?? '';
    this.newSchoolRedIzo = school.redIzo ?? '';
  }

  protected openAddSchoolDialog(): void {
    this.editingSchoolId = null;
    this.newSchoolName = '';
    this.newSchoolSlug = '';
    this.newAdminUsername = '';
    this.newAdminPassword = '';
    this.newSchoolIzo = '';
    this.newSchoolRedIzo = '';
    this.notify.clear();
    this.schoolDialogVisible.set(true);
  }

  protected saveSchool(): void {
    if (this.editingSchoolId !== null) {
      this.updateSchool(this.editingSchoolId);
      return;
    }
    if (
      !this.newSchoolName.trim() ||
      !this.newSchoolSlug.trim() ||
      !this.newAdminUsername.trim() ||
      !this.newAdminPassword.trim()
    ) {
      return;
    }
    const request: CreateSchoolRequest = {
      name: this.newSchoolName,
      slug: this.newSchoolSlug,
      adminUsername: this.newAdminUsername,
      adminPassword: this.newAdminPassword,
      izo: this.newSchoolIzo || undefined,
      redIzo: this.newSchoolRedIzo || undefined,
    };
    this.schoolsApi.addSchool(request).subscribe({
      next: (school) => {
        this.schoolDialogVisible.set(false);
        this.notify.success(`Škola ${school.name} založena.`);
        this.reload();
      },
      error: (err) => this.notify.actionFailed('addSchool failed', err),
    });
  }

  private updateSchool(id: number): void {
    const request: UpdateSchoolRequest = {
      name: this.newSchoolName.trim(),
      slug: this.newSchoolSlug.trim(),
      izo: this.newSchoolIzo.trim() || undefined,
      redIzo: this.newSchoolRedIzo.trim() || undefined,
    };
    this.schoolsApi.updateSchool(id, request).subscribe({
      next: () => {
        this.schoolDialogVisible.set(false);
        this.notify.success('Uloženo.');
        this.reload();
      },
      error: (err) => this.notify.actionFailed('updateSchool failed', err),
    });
  }

  protected deleteSchool(school: School): void {
    if (school.isDemo) {
      // A demo goes with all its data — ask first.
      this.confirmation.confirm({
        header: 'Smazat demo',
        message: `Smazat ukázkovou školu ${school.slug} se všemi daty?`,
        icon: 'pi pi-exclamation-triangle',
        acceptLabel: 'Smazat',
        rejectLabel: 'Zrušit',
        acceptButtonProps: { severity: 'danger' },
        rejectButtonProps: { severity: 'secondary', outlined: true },
        accept: () => this.doDeleteSchool(school),
      });
      return;
    }
    this.doDeleteSchool(school);
  }

  private doDeleteSchool(school: School): void {
    this.schoolsApi.deleteSchool(school.id).subscribe({
      next: () => {
        this.reload();
      },
      error: (err) => this.notify.actionFailed('deleteSchool failed', err),
    });
  }
}
