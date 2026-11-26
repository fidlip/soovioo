import {
  Component,
  computed,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  SUPPORT_TIME_CATEGORY_LABELS,
  SupportApiService,
  SupportEstimate,
  SupportEstimateStatus,
  SupportTicket,
  SupportTicketDetail,
  SupportTicketTime,
  SupportTimeCategory,
  SupportTimeEntry,
  formatAmount,
  formatSupportMinutes,
  isTicketOpen,
} from '@soovioo/shared';
import { Markdown, MarkdownEditor, NotifyService } from '@fidlip/soovioo-ui';
import { FORM_FIELDS, FloatField } from '../forms';

/** The support's time on a ticket as the backoffice counts it: what was
 * logged and how it counts, the school's month, the support's estimates of
 * work over the plan or of a paid service — the school's admin approves or
 * rejects them; anyone may dispute the category of logged time. */
@Component({
  selector: 'app-ticket-time',
  imports: [
    Button,
    DateFormatPipe,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Markdown,
    MarkdownEditor,
    Select,
    Tag,
  ],
  templateUrl: './ticket-time.html',
})
export class TicketTime {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);

  readonly ticket = input.required<SupportTicket>();
  readonly time = input.required<SupportTicketTime>();
  readonly readOnly = input(false);
  readonly changed = output<SupportTicketDetail>();

  protected readonly busy = signal(false);
  protected readonly minutes = formatSupportMinutes;
  protected readonly amount = formatAmount;
  protected readonly open = computed(() => isTicketOpen(this.ticket().status));
  /** Nothing to show yet: no time, no estimate, none asked for. */
  protected readonly empty = computed(() => {
    const time = this.time();
    return (
      !time.entries.length && !time.estimates.length && !time.estimateRequested
    );
  });
  /** Waiting for the support's estimate before any work. */
  protected readonly waitingForEstimate = computed(() => {
    const time = this.time();
    return (
      time.estimateRequested &&
      !time.estimates.some(
        (e) => e.status === 'Pending' || e.status === 'Approved',
      )
    );
  });
  /** Asking for an estimate makes sense before any work. */
  protected readonly mayRequestEstimate = computed(
    () =>
      this.open() &&
      !this.readOnly() &&
      !this.time().estimateRequested &&
      !this.time().estimates.length &&
      !this.time().entries.length,
  );

  protected categoryLabel(category: SupportTimeCategory): string {
    return SUPPORT_TIME_CATEGORY_LABELS[category];
  }

  protected estimateStatus(status: SupportEstimateStatus): {
    label: string;
    severity: 'warn' | 'success' | 'danger' | 'secondary';
  } {
    switch (status) {
      case 'Pending':
        return { label: 'Čeká na schválení', severity: 'warn' };
      case 'Approved':
        return { label: 'Schváleno', severity: 'success' };
      case 'Rejected':
        return { label: 'Odmítnuto', severity: 'danger' };
      default:
        return { label: 'Staženo', severity: 'secondary' };
    }
  }

  protected requestEstimate(): void {
    this.run(
      this.api.requestEstimate(this.ticket().id),
      'Podpora pošle odhad.',
    );
  }

  /** Approving or rejecting an estimate; rejecting says why. */
  protected readonly deciding = signal<{
    estimate: SupportEstimate;
    approve: boolean;
  } | null>(null);
  protected decisionNote = '';

  protected openDecision(estimate: SupportEstimate, approve: boolean): void {
    this.decisionNote = '';
    this.deciding.set({ estimate, approve });
  }

  protected decide(): void {
    const decision = this.deciding();
    if (!decision) {
      return;
    }
    this.run(
      this.api.decideEstimate(
        this.ticket().id,
        decision.estimate.id,
        decision.approve,
        this.decisionNote.trim(),
      ),
      decision.approve ? 'Odhad schválen.' : 'Odhad odmítnut.',
      () => this.deciding.set(null),
    );
  }

  /** Disputing the category of logged time. */
  protected readonly disputing = signal<SupportTimeEntry | null>(null);
  protected disputeCategory: SupportTimeCategory = 'Bug';
  protected disputeReason = '';
  protected readonly disputeOptions = computed(() => {
    const entry = this.disputing();
    return (Object.keys(SUPPORT_TIME_CATEGORY_LABELS) as SupportTimeCategory[])
      .filter((c) => c !== entry?.category)
      .map((value) => ({ value, label: SUPPORT_TIME_CATEGORY_LABELS[value] }));
  });

  protected mayDispute(entry: SupportTimeEntry): boolean {
    return !this.readOnly() && entry.dispute?.status !== 'Open';
  }

  protected openDispute(entry: SupportTimeEntry): void {
    this.disputeCategory = entry.category === 'Bug' ? 'Support' : 'Bug';
    this.disputeReason = '';
    this.disputing.set(entry);
  }

  protected dispute(): void {
    const entry = this.disputing();
    if (!entry) {
      return;
    }
    this.run(
      this.api.disputeTime(
        this.ticket().id,
        entry.id,
        this.disputeCategory,
        this.disputeReason.trim(),
      ),
      'Rozporováno, podpora se k tomu vyjádří.',
      () => this.disputing.set(null),
    );
  }

  private run(
    call: ReturnType<SupportApiService['requestEstimate']>,
    success: string,
    then?: () => void,
  ): void {
    this.busy.set(true);
    call.subscribe({
      next: (detail) => {
        this.busy.set(false);
        then?.();
        this.notify.success(success);
        this.changed.emit(detail);
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('ticket time action failed', err);
      },
    });
  }
}
