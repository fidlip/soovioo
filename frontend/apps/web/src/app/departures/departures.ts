import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService, PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  ErasureMode,
  FormerStaff,
  FormerStudent,
  RetentionApiService,
  RetentionCategory,
  RetentionOverview,
  RetentionPolicy,
  formatDate,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import { LeaveDialog, LeaveTarget } from './leave-dialog';
import {
  LEAVE_REASON_LABELS,
  RETENTION_CATEGORY_LABELS,
  RETENTION_YEAR_FIELDS,
} from './retention-labels';

/** Administrace → Archiv a výmaz údajů: pupils leaving or gone, staff who left, and
 * the school's rules for erasing their data (RetentionController). */
@Component({
  selector: 'app-departures',
  imports: [
    DateFormatPipe,
    FormsModule,
    PrimeTemplate,
    Button,
    Card,
    InputNumber,
    Select,
    TableModule,
    Tag,
    InfoHint,
    TermPipe,
    LeaveDialog,
    ...FORM_FIELDS,
  ],
  templateUrl: './departures.html',
})
export class Departures {
  private readonly retentionApi = inject(RetentionApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  protected readonly people = signal<RetentionOverview | null>(null);
  protected readonly policy = signal<RetentionPolicy | null>(null);
  protected readonly editing = signal<LeaveTarget | null>(null);

  protected readonly dueCount = computed(() => {
    const people = this.people();
    return people
      ? people.students.filter((s) => s.due.length > 0).length +
          people.staff.filter((s) => s.due).length
      : 0;
  });

  protected readonly yearFields = RETENTION_YEAR_FIELDS;
  protected readonly categoryLabels = RETENTION_CATEGORY_LABELS;
  protected readonly reasonLabels = LEAVE_REASON_LABELS;
  protected readonly modes: { label: string; value: ErasureMode }[] = [
    { label: 'Výmaz potvrzuje administrátor', value: 'Confirm' },
    { label: 'Automaticky každý den', value: 'Automatic' },
  ];

  constructor() {
    this.load();
    this.retentionApi.getPolicy().subscribe({
      next: (policy) => this.policy.set(policy),
      error: (err) => this.notify.actionFailed('getPolicy failed', err),
    });
  }

  protected load(): void {
    this.retentionApi.getPeople().subscribe({
      next: (people) => this.people.set(people),
      error: (err) => this.notify.actionFailed('getPeople failed', err),
    });
  }

  protected labels(categories: RetentionCategory[]): string {
    return categories.map((c) => this.categoryLabels[c]).join(', ');
  }

  protected reasonAndNote(student: FormerStudent): string {
    const reason =
      student.reason === 'Other' && student.reasonDetail
        ? student.reasonDetail
        : student.reason
          ? this.reasonLabels[student.reason]
          : null;
    return [reason, student.note].filter((x) => x).join(' — ');
  }

  /** The next erasure still ahead (not due yet). */
  protected next(student: FormerStudent): string {
    const next = student.pending.find((p) => !student.due.includes(p.category));
    return next
      ? `${this.categoryLabels[next.category]} ${formatDate(next.on, 'date')}`
      : '—';
  }

  protected leaveChanged(): void {
    this.editing.set(null);
    this.load();
  }

  protected eraseDueOf(student: FormerStudent): void {
    this.confirm(
      'Vymazat údaje',
      `Vymazat u ${student.firstName} ${student.lastName}: ${this.labels(student.due)}? Nejde to vrátit.`,
      () =>
        this.retentionApi.eraseStudent(student.id, false).subscribe({
          next: () => this.erased(),
          error: (err) => this.notify.actionFailed('eraseStudent failed', err),
        }),
    );
  }

  protected eraseEverything(student: FormerStudent): void {
    this.confirm(
      'Vymazat vše',
      `Vymazat všechny údaje ${student.firstName} ${student.lastName} hned, bez ohledu na lhůty (např. na žádost podle čl. 17 GDPR)? Zmizí i jeho účet a účty rodičů, kterým ve škole nezůstane jiné dítě. Nejde to vrátit.`,
      () =>
        this.retentionApi.eraseStudent(student.id, true).subscribe({
          next: () => this.erased(),
          error: (err) => this.notify.actionFailed('eraseStudent failed', err),
        }),
    );
  }

  protected eraseStaff(staff: FormerStaff): void {
    this.confirm(
      'Vymazat údaje',
      `Vymazat údaje ${staff.name} (jméno, kontakty, fotku) a zrušit účet? Nejde to vrátit.`,
      () =>
        this.retentionApi.eraseStaff(staff.userId).subscribe({
          next: () => this.erased(),
          error: (err) => this.notify.actionFailed('eraseStaff failed', err),
        }),
    );
  }

  protected eraseAllDue(): void {
    this.confirm(
      'Provést výmazy',
      'Vymazat všechno, čemu uplynula lhůta? Nejde to vrátit.',
      () =>
        this.retentionApi.eraseDue().subscribe({
          next: () => this.erased(),
          error: (err) => this.notify.actionFailed('eraseDue failed', err),
        }),
    );
  }

  protected setYears(field: keyof RetentionPolicy, value: number | null): void {
    this.policy.update((p) => (p ? { ...p, [field]: value ?? 0 } : p));
  }

  protected yearsOf(field: keyof RetentionPolicy): number {
    return this.policy()?.[field] as number;
  }

  protected savePolicy(): void {
    const policy = this.policy();
    if (!policy) {
      return;
    }
    this.retentionApi.setPolicy(policy).subscribe({
      next: (saved) => {
        this.policy.set(saved);
        this.notify.success('Uloženo.');
        this.load();
      },
      error: (err) => this.notify.actionFailed('setPolicy failed', err),
    });
  }

  private erased(): void {
    this.notify.success('Vymazáno.');
    this.load();
  }

  private confirm(header: string, message: string, accept: () => void): void {
    this.confirmation.confirm({
      header,
      message,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Vymazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept,
    });
  }
}
