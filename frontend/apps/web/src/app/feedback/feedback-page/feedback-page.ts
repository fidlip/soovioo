import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ConfirmationService, PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import {
  DateFormatPipe,
  Feedback,
  FeedbackApiService,
  FeedbackReply,
  FeedbackSource,
  FeedbackState,
  FeedbackUpdate,
} from '@soovioo/shared';
import { AppIcon } from '../../app-icon';
import { ClearFilters, FloatField } from '../../forms';
import { Loading } from '../../loading/loading';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';

/** Values recorded before roles became the school's own (now the role's name). */
const LEGACY_ROLE_LABELS: Record<string, string> = {
  SuperAdmin: 'Superadmin',
  Admin: 'Admin',
  Teacher: 'Učitel',
  Parent: 'Rodič',
  Student: 'Žák',
};

/** SuperAdmin's "Zpětná vazba" — what schools' users sent from Nastavení
 * and demo visitors from the demo's dialog. Opening one marks it seen (for
 * everyone); it can be flagged, resolved, answered by e-mail (to whoever
 * agreed to be contacted) and deleted. */
@Component({
  selector: 'app-feedback-page',
  imports: [
    AppIcon,
    Button,
    Card,
    ClearFilters,
    DateFormatPipe,
    Dialog,
    FloatField,
    FormsModule,
    InputText,
    Loading,
    Paginator,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
    Textarea,
  ],
  templateUrl: './feedback-page.html',
})
export class FeedbackPage {
  private readonly feedbackApi = inject(FeedbackApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);
  private readonly unread = inject(UnreadCountsService);

  protected readonly items = signal<Feedback[]>([]);
  protected readonly totalCount = signal(0);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(20);
  protected readonly loading = signal(false);
  protected readonly loaded = signal(false);

  protected readonly stateOptions: {
    label: string;
    value: FeedbackState;
  }[] = [
    { label: 'Nové', value: 'Unseen' },
    { label: 'Bez odpovědi (se souhlasem)', value: 'Unanswered' },
    { label: 'Zodpovězené', value: 'Answered' },
    { label: 'Důležité', value: 'Flagged' },
    { label: 'Nevyřízené', value: 'Open' },
    { label: 'Vyřízené', value: 'Resolved' },
  ];
  protected readonly sourceOptions: { label: string; value: FeedbackSource }[] =
    [
      { label: 'Ze škol', value: 'School' },
      { label: 'Z dem', value: 'Demo' },
    ];
  protected readonly ratingOptions = [
    ...[5, 4, 3, 2, 1].map((r) => ({ label: this.stars(r), value: r })),
    { label: 'Bez hodnocení', value: 0 },
  ];

  protected filterState: FeedbackState | null = null;
  protected filterSource: FeedbackSource | null = null;
  protected filterRating: number | null = null;
  protected search = '';
  private searchTimer: ReturnType<typeof setTimeout> | undefined;
  private listRequest: Subscription | undefined;

  protected readonly selected = signal<Feedback | null>(null);
  protected readonly replies = signal<FeedbackReply[] | null>(null);
  protected replyText = '';
  protected readonly sending = signal(false);

  constructor() {
    this.reload();
  }

  protected onFilterChange(): void {
    this.page.set(1);
    this.reload();
  }

  protected onSearchChange(): void {
    clearTimeout(this.searchTimer);
    this.searchTimer = setTimeout(() => this.onFilterChange(), 400);
  }

  protected hasFilters(): boolean {
    return (
      this.filterState !== null ||
      this.filterSource !== null ||
      this.filterRating !== null ||
      this.search.trim() !== ''
    );
  }

  protected clearFilters(): void {
    clearTimeout(this.searchTimer);
    this.filterState = null;
    this.filterSource = null;
    this.filterRating = null;
    this.search = '';
    this.onFilterChange();
  }

  protected onPageChange(event: PaginatorState): void {
    this.pageSize.set(event.rows ?? this.pageSize());
    this.page.set((event.page ?? 0) + 1);
    this.reload();
  }

  protected stars(rating: number | null): string {
    return rating ? '★'.repeat(rating) + '☆'.repeat(5 - rating) : '—';
  }

  protected roleLabel(role: string | null): string {
    return role ? (LEGACY_ROLE_LABELS[role] ?? role) : '—';
  }

  /** Opening it is reading it: seen for every SuperAdmin from now on. */
  protected open(item: Feedback): void {
    if (window.getSelection()?.toString()) {
      return;
    }
    this.selected.set(item);
    this.replyText = '';
    this.replies.set(null);
    this.feedbackApi.replies(item.id).subscribe({
      next: (replies) => this.replies.set(replies),
      error: (err) => {
        this.notify.actionFailed('feedback replies failed', err);
        this.replies.set([]);
      },
    });
    if (!item.seenAt) {
      this.update(item, { seen: true });
    }
  }

  protected close(): void {
    this.selected.set(null);
  }

  protected markUnseen(item: Feedback): void {
    this.update(item, { seen: false });
    this.close();
  }

  protected toggleFlag(item: Feedback): void {
    this.update(item, { flagged: !item.flagged });
  }

  protected remove(item: Feedback): void {
    this.confirmation.confirm({
      header: 'Smazat zpětnou vazbu',
      message:
        'Smazat zpětnou vazbu i s odeslanými odpověďmi? Nejde to vrátit.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.feedbackApi.delete(item.id).subscribe({
          next: () => {
            this.close();
            this.notify.success('Zpětná vazba smazána.');
            if (!item.seenAt) {
              this.unread.refreshFeedback();
            }
            // The page may now be short of a row, or empty.
            if (this.items().length === 1 && this.page() > 1) {
              this.page.update((p) => p - 1);
            }
            this.reload();
          },
          error: (err) =>
            this.notify.actionFailed('feedback delete failed', err),
        }),
    });
  }

  /** Demo slug or the school's name, and whether it's gone. */
  protected source(item: Feedback): string {
    return item.demoSlug ?? item.schoolName ?? '—';
  }

  protected toggleResolved(item: Feedback): void {
    this.update(item, { resolved: !item.resolvedAt });
  }

  protected sendReply(item: Feedback): void {
    const text = this.replyText.trim();
    if (!text || this.sending()) {
      return;
    }
    this.sending.set(true);
    this.feedbackApi.reply(item.id, text).subscribe({
      next: (reply) => {
        this.sending.set(false);
        this.replyText = '';
        this.replies.update((list) => [...(list ?? []), reply]);
        this.replace({
          ...item,
          seenAt: item.seenAt ?? reply.sentAt,
          replyCount: item.replyCount + 1,
          lastReplyAt: reply.sentAt,
        });
        this.notify.success(`Odpověď odeslána na ${item.email}.`);
      },
      error: (err) => {
        this.sending.set(false);
        this.notify.actionFailed('feedback reply failed', err);
      },
    });
  }

  private update(item: Feedback, update: FeedbackUpdate): void {
    this.feedbackApi.update(item.id, update).subscribe({
      next: (updated) => {
        this.replace(updated);
        if (update.seen !== undefined) {
          this.unread.refreshFeedback();
        }
      },
      error: (err) => this.notify.actionFailed('feedback update failed', err),
    });
  }

  /** The row and the open dialog show the new state. */
  private replace(updated: Feedback): void {
    this.items.update((list) =>
      list.map((f) => (f.id === updated.id ? updated : f)),
    );
    if (this.selected()?.id === updated.id) {
      this.selected.set(updated);
    }
  }

  private reload(): void {
    this.loading.set(true);
    // A slower answer to an older filter must not replace this one.
    this.listRequest?.unsubscribe();
    this.listRequest = this.feedbackApi
      .list({
        state: this.filterState,
        source: this.filterSource,
        rating: this.filterRating,
        search: this.search.trim() || null,
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (result) => {
          this.items.set(result.items);
          this.totalCount.set(result.totalCount);
          this.loading.set(false);
          this.loaded.set(true);
        },
        error: (err) => {
          this.notify.actionFailed('demo feedback failed', err);
          this.loading.set(false);
          this.loaded.set(true);
        },
      });
  }
}
