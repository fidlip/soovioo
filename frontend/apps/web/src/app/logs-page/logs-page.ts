import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import {
  AuditLogApiService,
  AuditLogEntry,
  AuditLogOption,
  AuthApiService,
  SchoolsApiService,
  SupportApiService,
  SupportSession,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';

const ROLE_LABELS: Record<string, string> = {
  SuperAdmin: 'superadmin',
  Admin: 'admin',
  Teacher: 'učitel',
  Parent: 'rodič',
  Student: 'žák',
};

/** Agenda "Logy" — the audit log of changes and logins. A SuperAdmin sees
 * every school (filterable by school) plus the support-mode visits; a
 * school admin sees only their own school, support-mode changes flagged. */
@Component({
  selector: 'app-logs-page',
  imports: [
    AppIcon,
    Button,
    Card,
    DatePicker,
    DatePipe,
    FormsModule,
    Paginator,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
  ],
  templateUrl: './logs-page.html',
})
export class LogsPage {
  private readonly auditApi = inject(AuditLogApiService);
  private readonly supportApi = inject(SupportApiService);
  private readonly schoolsApi = inject(SchoolsApiService);
  protected readonly isSuperAdmin = inject(AuthApiService).isSuperAdmin();

  protected readonly entries = signal<AuditLogEntry[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(50);
  protected readonly loading = signal(false);
  protected expandedRows: Record<number, boolean> = {};

  protected readonly schoolOptions = signal<AuditLogOption[]>([]);
  protected readonly classOptions = signal<AuditLogOption[]>([]);
  protected readonly userOptions = signal<AuditLogOption[]>([]);
  protected readonly areaOptions = signal<string[]>([]);

  protected filterSchoolId: number | null = null;
  protected filterClassId: number | null = null;
  protected filterUserId: number | null = null;
  protected filterArea: string | null = null;
  protected filterFrom: Date | null = null;
  protected filterTo: Date | null = null;

  protected readonly supportSessions = signal<SupportSession[]>([]);

  constructor() {
    this.loadFilterOptions();
    this.reload();
    if (this.isSuperAdmin) {
      this.schoolsApi.getSchools(1, 100).subscribe({
        next: (result) =>
          this.schoolOptions.set(
            result.items.map((s) => ({ id: s.id, name: s.name })),
          ),
        error: (err) => console.error('getSchools failed', err),
      });
      this.supportApi.getSupportSessions().subscribe({
        next: (sessions) => this.supportSessions.set(sessions),
        error: (err) => console.error('getSupportSessions failed', err),
      });
    }
  }

  protected onSchoolChange(): void {
    // Classes and people differ per school.
    this.filterClassId = null;
    this.filterUserId = null;
    this.loadFilterOptions();
    this.onFilterChange();
  }

  protected onFilterChange(): void {
    this.page.set(1);
    this.reload();
  }

  protected clearFilters(): void {
    this.filterSchoolId = null;
    this.filterClassId = null;
    this.filterUserId = null;
    this.filterArea = null;
    this.filterFrom = null;
    this.filterTo = null;
    this.loadFilterOptions();
    this.onFilterChange();
  }

  protected hasFilters(): boolean {
    return (
      this.filterSchoolId !== null ||
      this.filterClassId !== null ||
      this.filterUserId !== null ||
      this.filterArea !== null ||
      this.filterFrom !== null ||
      this.filterTo !== null
    );
  }

  protected onPageChange(event: PaginatorState): void {
    this.pageSize.set(event.rows ?? this.pageSize());
    this.page.set((event.page ?? 0) + 1);
    this.reload();
  }

  protected who(entry: AuditLogEntry): string {
    return entry.userDisplayName ?? entry.username ?? 'nepřihlášený';
  }

  protected roleLabel(role: string | null): string | null {
    return role ? (ROLE_LABELS[role] ?? role) : null;
  }

  protected statusSeverity(status: number): 'success' | 'warn' | 'danger' {
    return status >= 500 ? 'danger' : status >= 400 ? 'warn' : 'success';
  }

  /** The payload pretty-printed when it's JSON. */
  protected formatBody(body: string): string {
    try {
      return JSON.stringify(JSON.parse(body), null, 2);
    } catch {
      return body;
    }
  }

  protected changesLabel(count: number): string {
    return count === 1
      ? '1 změna'
      : count >= 2 && count <= 4
        ? `${count} změny`
        : `${count} změn`;
  }

  private loadFilterOptions(): void {
    this.auditApi.getFilterOptions(this.filterSchoolId).subscribe({
      next: (options) => {
        this.areaOptions.set(options.areas);
        this.userOptions.set(options.users);
        this.classOptions.set(options.classes);
      },
      error: (err) => console.error('getFilterOptions failed', err),
    });
  }

  private reload(): void {
    this.loading.set(true);
    // Whole local days: from midnight of "Od" up to midnight after "Do".
    const to = this.filterTo ? new Date(this.filterTo) : null;
    to?.setDate(to.getDate() + 1);
    this.auditApi
      .getEntries({
        schoolId: this.filterSchoolId,
        schoolClassId: this.filterClassId,
        userId: this.filterUserId,
        area: this.filterArea,
        from: this.filterFrom?.toISOString() ?? null,
        to: to?.toISOString() ?? null,
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (result) => {
          this.entries.set(result.items);
          this.totalCount.set(result.totalCount);
          this.expandedRows = {};
          this.loading.set(false);
        },
        error: (err) => {
          console.error('getEntries failed', err);
          this.loading.set(false);
        },
      });
  }
}
