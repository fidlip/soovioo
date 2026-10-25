import { DatePipe, NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import {
  AutoComplete,
  AutoCompleteCompleteEvent,
  AutoCompleteSelectEvent,
} from '@openng/optimus-ui/autocomplete';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import {
  AuditLogApiService,
  AuditLogEntry,
  AuditLogOption,
  AuditLogQuery,
  AuthApiService,
  SchoolsApiService,
  SupportApiService,
  SupportSession,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { GroupLevel, GroupedTable } from '../grouped-table/grouped-table';

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
    AutoComplete,
    Button,
    Card,
    DatePicker,
    DatePipe,
    FormsModule,
    GroupedTable,
    NgTemplateOutlet,
    Paginator,
    Select,
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
  /** First page arrived (or failed) — see the template. */
  protected readonly loaded = signal(false);
  private readonly expandedIds = signal<ReadonlySet<number>>(new Set());
  /** A new function whenever the set changes, so the table re-renders. */
  protected readonly isExpanded = computed(() => {
    const ids = this.expandedIds();
    return (entry: AuditLogEntry) => ids.has(entry.id);
  });
  protected readonly entryId = (entry: AuditLogEntry) => entry.id;

  /** Consecutive runs (the log is newest first): day → who → agenda. */
  protected readonly groups: GroupLevel<AuditLogEntry>[] = [
    {
      label: 'Datum',
      key: (e) => localDay(e.at),
      text: (e) => localDay(e.at),
    },
    {
      label: 'Kdo',
      key: (e) => `${e.userId ?? e.username}|${e.role}|${e.supportBy}`,
      text: (e) =>
        [
          this.who(e),
          this.roleLabel(e.role),
          e.supportBy ? `podpora (superadmin ${e.supportBy})` : null,
        ]
          .filter(Boolean)
          .join(' · '),
    },
    { label: 'Agenda', key: (e) => e.area, text: (e) => e.area },
  ];

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
  /** "Akce": picked from the list = that exact action; typed = actions
   * and descriptions containing the text. */
  protected readonly actionText = signal('');
  private actionExact = false;
  private actionSearchTimer: ReturnType<typeof setTimeout> | undefined;
  private entriesRequest: Subscription | undefined;
  protected readonly actionSuggestions = signal<string[]>([]);

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
    this.actionText.set('');
    this.actionExact = false;
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
      this.filterTo !== null ||
      this.actionText() !== ''
    );
  }

  /** Offers the actions that occur under the other active filters. */
  protected searchActions(event: AutoCompleteCompleteEvent): void {
    this.auditApi.getActions(this.query(), event.query).subscribe({
      next: (actions) => this.actionSuggestions.set(actions),
      error: (err) => console.error('getActions failed', err),
    });
  }

  /** Typing filters by contained text (debounced). */
  protected onActionTyped(text: string | null): void {
    this.actionText.set(text ?? '');
    this.actionExact = false;
    clearTimeout(this.actionSearchTimer);
    this.actionSearchTimer = setTimeout(() => this.onFilterChange(), 400);
  }

  /** Picking from the list filters by that exact action. */
  protected onActionSelected(event: AutoCompleteSelectEvent): void {
    clearTimeout(this.actionSearchTimer);
    this.actionText.set(event.value as string);
    this.actionExact = true;
    this.onFilterChange();
  }

  protected onActionCleared(): void {
    clearTimeout(this.actionSearchTimer);
    this.actionText.set('');
    this.actionExact = false;
    this.onFilterChange();
  }

  protected onPageChange(event: PaginatorState): void {
    this.pageSize.set(event.rows ?? this.pageSize());
    this.page.set((event.page ?? 0) + 1);
    this.reload();
  }

  /** Click anywhere on the row — except when the click ends a text
   * selection (copying a value out of the row). */
  protected toggleRow(entry: AuditLogEntry): void {
    if (window.getSelection()?.toString()) {
      return;
    }
    this.expandedIds.update((ids) => {
      const next = new Set(ids);
      if (!next.delete(entry.id)) {
        next.add(entry.id);
      }
      return next;
    });
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

  /** The active filters except the action. */
  private query(): AuditLogQuery {
    // Whole local days: from midnight of "Od" up to midnight after "Do".
    const to = this.filterTo ? new Date(this.filterTo) : null;
    to?.setDate(to.getDate() + 1);
    return {
      schoolId: this.filterSchoolId,
      schoolClassId: this.filterClassId,
      userId: this.filterUserId,
      area: this.filterArea,
      from: this.filterFrom?.toISOString() ?? null,
      to: to?.toISOString() ?? null,
    };
  }

  private reload(): void {
    this.loading.set(true);
    const action = this.actionText().trim();
    // A slower answer to an older filter must not replace this one.
    this.entriesRequest?.unsubscribe();
    this.entriesRequest = this.auditApi
      .getEntries({
        ...this.query(),
        action: this.actionExact ? action : null,
        actionSearch: this.actionExact ? null : action,
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (result) => {
          this.entries.set(result.items);
          this.totalCount.set(result.totalCount);
          this.expandedIds.set(new Set());
          this.loading.set(false);
          this.loaded.set(true);
        },
        error: (err) => {
          console.error('getEntries failed', err);
          this.loading.set(false);
          this.loaded.set(true);
        },
      });
  }
}

/** "19.10.2026" in the viewer's time zone. */
function localDay(iso: string): string {
  const d = new Date(iso);
  return `${d.getDate()}.${d.getMonth() + 1}.${d.getFullYear()}`;
}
