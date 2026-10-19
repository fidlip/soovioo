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
import {
  CreateSchoolRequest,
  School,
  SchoolsApiService,
  UpdateSchoolRequest,
} from '@soovioo/shared';
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
  ],
  templateUrl: './super-admin-page.html',
})
export class SuperAdminPage {
  protected readonly notify = inject(NotifyService);
  private readonly schoolsApi = inject(SchoolsApiService);

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
