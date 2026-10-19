import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Password } from '@openng/optimus-ui/password';
import { TableModule } from '@openng/optimus-ui/table';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import { Router } from '@angular/router';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  CreateSchoolRequest,
  School,
  SchoolsApiService,
  SuperAdmin,
  SupportApiService,
  SupportSession,
  UpdateSchoolRequest,
} from '@soovioo/shared';
import { isPathBasedTenant } from '../school-context';
import { AppIcon } from '../app-icon';
import { NotifyService } from '../notify.service';
import { FORM_FIELDS } from '../forms';

const DEFAULT_PAGE_SIZE = 10;

@Component({
  selector: 'app-super-admin-page',
  imports: [
    ...FORM_FIELDS,
    AppIcon,
    DatePipe,
    FormsModule,
    Button,
    Card,
    InputText,
    Paginator,
    Password,
    PrimeTemplate,
    TableModule,
    Tag,
  ],
  templateUrl: './super-admin-page.html',
})
export class SuperAdminPage {
  protected readonly notify = inject(NotifyService);
  private readonly schoolsApi = inject(SchoolsApiService);
  private readonly supportApi = inject(SupportApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly router = inject(Router);

  protected readonly superAdmins = signal<SuperAdmin[]>([]);
  protected readonly supportSessions = signal<SupportSession[]>([]);
  protected readonly myUserId = this.authApi.getUserId();
  protected newSuperAdminUsername = '';
  protected newSuperAdminPassword = '';

  protected readonly schools = signal<School[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(DEFAULT_PAGE_SIZE);

  protected search = '';

  protected newSchoolName = '';
  protected newSchoolSlug = '';
  protected newAdminUsername = '';
  protected newAdminPassword = '';
  protected newSchoolIzo = '';
  protected newSchoolRedIzo = '';

  protected editingSchoolId: number | null = null;
  protected editName = '';
  protected editSlug = '';

  /** A signal, not a plain field — this app has no zone.js polyfill, so a
   * plain-field write inside an RxJS subscribe callback doesn't reliably
   * schedule change detection on its own. */

  constructor() {
    this.reload();
    this.reloadSuperAdmins();
    this.supportApi.getSupportSessions().subscribe({
      next: (sessions) => this.supportSessions.set(sessions),
      error: (err) => console.error('getSupportSessions failed', err),
    });
  }

  /** "Vstoupit do školy": an audited, 2-hour Admin session in that school
   * (support mode); the own SuperAdmin session comes back on "Opustit". */
  protected enterSchool(school: School): void {
    if (!isPathBasedTenant()) {
      // Each school lives on its own subdomain there and the token is kept
      // in memory only — it can't follow the jump yet (see ROADMAP).
      this.notify.error(
        'Vstup do školy zatím funguje jen při provozu s adresou školy v cestě (/slug).',
      );
      return;
    }
    this.supportApi.startSupportSession(school.id).subscribe({
      next: (session) => {
        this.authApi.store({ token: session.token });
        this.router.navigate(['/', session.schoolSlug]);
      },
      error: (err) => this.notify.actionFailed('startSupportSession failed', err),
    });
  }

  protected addSuperAdmin(): void {
    this.supportApi
      .addSuperAdmin({
        username: this.newSuperAdminUsername.trim(),
        password: this.newSuperAdminPassword,
      })
      .subscribe({
        next: (added) => {
          this.notify.success(
            `Superadmin ${added.username} přidán — při prvním přihlášení si změní heslo.`,
          );
          this.newSuperAdminUsername = '';
          this.newSuperAdminPassword = '';
          this.reloadSuperAdmins();
        },
        error: (err) => this.notify.actionFailed('addSuperAdmin failed', err),
      });
  }

  protected deleteSuperAdmin(superAdmin: SuperAdmin): void {
    this.supportApi.deleteSuperAdmin(superAdmin.id).subscribe({
      next: () => this.reloadSuperAdmins(),
      error: (err) => this.notify.actionFailed('deleteSuperAdmin failed', err),
    });
  }

  protected changesLabel(count: number): string {
    return count === 1
      ? '1 změna'
      : count >= 2 && count <= 4
        ? `${count} změny`
        : `${count} změn`;
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
      .getSchools(this.page(), this.pageSize(), this.search.trim() || undefined)
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
        },
        error: (err) => console.error('getSchools failed', err),
      });
  }

  protected addSchool(): void {
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
      next: () => {
        this.newSchoolName = '';
        this.newSchoolSlug = '';
        this.newAdminUsername = '';
        this.newAdminPassword = '';
        this.newSchoolIzo = '';
        this.newSchoolRedIzo = '';
        this.reload();
      },
      error: (err) => this.notify.actionFailed('addSchool failed', err),
    });
  }

  protected startEdit(school: School): void {
    this.editingSchoolId = school.id;
    this.editName = school.name;
    this.editSlug = school.slug;
  }

  protected cancelEdit(): void {
    this.editingSchoolId = null;
  }

  protected saveEdit(): void {
    if (
      this.editingSchoolId === null ||
      !this.editName.trim() ||
      !this.editSlug.trim()
    ) {
      return;
    }
    const request: UpdateSchoolRequest = {
      name: this.editName,
      slug: this.editSlug,
    };
    this.schoolsApi.updateSchool(this.editingSchoolId, request).subscribe({
      next: () => {
        this.editingSchoolId = null;
        this.reload();
      },
      error: (err) => this.notify.actionFailed('updateSchool failed', err),
    });
  }

  protected deleteSchool(school: School): void {
    this.schoolsApi.deleteSchool(school.id).subscribe({
      next: () => {
        this.reload();
      },
      error: (err) => this.notify.actionFailed('deleteSchool failed', err),
    });
  }
}
