import {
  Component,
  DestroyRef,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { FormsModule, NgForm } from '@angular/forms';
import { ConfirmationService, MenuItem } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Menu } from '@openng/optimus-ui/menu';
import {
  DateFormatPipe,
  SUPPORT_RESET_ACTION_LABELS,
  SUPPORT_TICKET_STATUS_LABELS,
  SupportApiService,
  SupportResetItem,
  SupportResetPreview,
  SupportTicket,
  SupportTicketDetail,
  SupportTicketEntry,
  changesLabel,
  formatDate,
  isTicketOpen,
} from '@soovioo/shared';
import {
  Observable,
  Subscription,
  filter,
  fromEvent,
  interval,
  merge,
} from 'rxjs';
import { FORM_FIELDS } from '../forms';
import { LiveService } from '../live.service';
import {
  Markdown,
  MarkdownEditor,
  NotifyService,
  TicketStatusTag,
} from '@fidlip/soovioo-ui';
import {
  TicketVisit,
  formatMinutes,
  groupTicketThread,
  hiddenWhenFolded,
  visitMinutes,
} from './ticket-thread';
import { TicketAccess } from './ticket-access';
import { TicketTime } from './ticket-time';

/** The school's support ticket with its thread (kept in the backoffice):
 * answering, closing or returning it, cancelling, the support's time and
 * estimates, and what the support did inside the school — its visits and
 * their reset. */
@Component({
  selector: 'app-support-ticket-dialog',
  imports: [
    Button,
    Checkbox,
    DateFormatPipe,
    Dialog,
    ...FORM_FIELDS,
    FormsModule,
    Markdown,
    MarkdownEditor,
    NgTemplateOutlet,
    Menu,
    TicketStatusTag,
    TicketAccess,
    TicketTime,
  ],
  templateUrl: './support-ticket-dialog.html',
})
export class SupportTicketDialog {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly confirmation = inject(ConfirmationService);

  /** In support mode: looking only. */
  readonly readOnly = input(false);
  /** The ticket as it is after a change — for the list. */
  readonly changed = output<SupportTicket>();

  protected readonly detail = signal<SupportTicketDetail | null>(null);
  protected readonly visible = signal(false);
  protected readonly busy = signal(false);
  protected text = '';
  protected readonly cancelling = signal(false);
  protected cancelReason = '';
  /** Cancelling resets the support's changes first. */
  protected cancelReset = false;
  /** Resetting the support's changes: of the ticket (sessionId null) or of one visit. */
  protected readonly resetting = signal<{ sessionId: number | null } | null>(
    null,
  );
  protected resetReason = '';
  /** What the reset does — for resetting or cancelling with it. */
  protected readonly resetPreview = signal<SupportResetPreview | null>(null);
  protected readonly resetReturned = computed(
    () => this.resetPreview()?.items.filter((i) => !i.skipped) ?? [],
  );
  protected readonly resetSkipped = computed(
    () => this.resetPreview()?.items.filter((i) => i.skipped) ?? [],
  );
  protected readonly returning = signal(false);
  protected returnText = '';
  /** The message being changed in place. */
  protected readonly editing = signal<number | null>(null);
  protected editText = '';
  protected readonly entryMenuItems = signal<MenuItem[]>([]);
  protected readonly items = computed(() =>
    groupTicketThread(this.detail()?.entries ?? []),
  );
  /** Only the last visit is unfolded (and a running one) until the user
   * folds or unfolds one — kept while the ticket stays open. */
  private readonly folds = signal(new Map<number, boolean>());
  private readonly lastVisit = computed(() => {
    const ids = this.items()
      .map((i) => i.visit?.sessionId)
      .filter((id) => id !== undefined);
    return ids.length ? ids[ids.length - 1] : null;
  });
  protected readonly changesLabel = changesLabel;
  /** Ticks every minute while open — a running visit's time stays current. */
  private readonly tick = signal(0);
  private readonly now = computed(
    () => (this.detail(), this.tick(), Date.now()),
  );
  /** All the visits together, a running one until now. */
  protected readonly visitsTotal = computed(() => {
    const now = this.now();
    const visits = this.items().filter((i) => i.visit);
    return visits.length
      ? formatMinutes(
          visits.reduce((sum, i) => sum + visitMinutes(i.visit!, now), 0),
        )
      : null;
  });
  private readonly thread = viewChild<ElementRef<HTMLElement>>('thread');
  private readonly injector = inject(Injector);
  private readonly live = inject(LiveService);
  private polling?: Subscription;
  private unwatch?: () => void;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopRefreshing());
  }

  protected readonly ticket = computed(() => this.detail()?.ticket ?? null);
  protected readonly ticketOpen = computed(() => {
    const ticket = this.ticket();
    return !!ticket && isTicketOpen(ticket.status);
  });
  protected readonly canManage = computed(
    () => this.ticketOpen() && !this.readOnly(),
  );
  /** The support's changes the caller may reset — not when only looking. */
  protected readonly resettable = computed(() =>
    this.readOnly() ? 0 : (this.ticket()?.resettableChanges ?? 0),
  );
  /** The last day reopening is possible (it ends at its midnight). */
  protected readonly reopenLastDay = computed(() => {
    const until = this.ticket()?.reopenableUntil;
    return until ? new Date(new Date(until).getTime() - 1).toISOString() : null;
  });

  open(id: number): void {
    this.text = '';
    this.folds.set(new Map());
    this.notify.clear();
    this.api.getSchoolTicket(id).subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.visible.set(true);
        this.scrollToNewest();
        this.startRefreshing();
      },
      error: (err) => this.notify.actionFailed('getTicket failed', err),
    });
  }

  protected setVisible(visible: boolean): void {
    this.visible.set(visible);
    if (!visible) {
      this.stopRefreshing();
    }
  }

  /** The support changes the ticket too (answers, logs time, sends an
   * estimate…), a visit goes inactive: while open, it reloads when the live
   * connection says it changed (without it now and then, and when the tab
   * is back in front). */
  private startRefreshing(): void {
    this.polling?.unsubscribe();
    this.unwatch?.();
    const id = this.ticket()?.id;
    if (!id) {
      return;
    }
    this.unwatch = this.live.watchTicket(id);
    const offline = () =>
      !this.live.connected() && document.visibilityState === 'visible';
    this.polling = merge(
      this.live.ticket$(id),
      interval(15_000).pipe(filter(offline)),
      fromEvent(document, 'visibilitychange').pipe(filter(offline)),
    ).subscribe(() => this.reload());
    this.polling.add(
      interval(60_000).subscribe(() => this.tick.update((t) => t + 1)),
    );
  }

  private stopRefreshing(): void {
    this.polling?.unsubscribe();
    this.polling = undefined;
    this.unwatch?.();
    this.unwatch = undefined;
  }

  private reload(): void {
    const ticket = this.ticket();
    if (!ticket || !this.visible()) {
      return;
    }
    // Saving something of its own — the change may be someone else's.
    if (this.busy()) {
      setTimeout(() => this.reload(), 1000);
      return;
    }
    this.api.getSchoolTicket(ticket.id).subscribe({
      next: (detail) => {
        if (this.visible() && !this.busy()) {
          const atNewest = this.atNewest();
          this.detail.set(detail);
          if (atNewest) {
            this.scrollToNewest();
          }
          this.changed.emit(detail.ticket);
        }
      },
      error: () => undefined,
    });
  }

  /** The thread reads from the oldest; opened or added to, it shows the newest. */
  private scrollToNewest(): void {
    afterNextRender(
      () => {
        const el = this.thread()?.nativeElement;
        if (el) {
          el.scrollTop = el.scrollHeight;
        }
      },
      { injector: this.injector },
    );
  }

  private atNewest(): boolean {
    const el = this.thread()?.nativeElement;
    return !el || el.scrollHeight - el.scrollTop - el.clientHeight < 8;
  }

  /** The line a system entry shows. */
  protected entryLabel(entry: SupportTicketEntry): string {
    switch (entry.kind) {
      case 'Created':
        return 'Požadavek založen';
      case 'Assigned':
        return 'Převzato podporou';
      case 'Released':
        return 'Podpora požadavek vrátila k převzetí';
      case 'HandedOver':
        return 'Předáno jinému řešiteli';
      case 'StatusChanged':
        return `Stav: ${entry.status ? SUPPORT_TICKET_STATUS_LABELS[entry.status] : ''}`;
      case 'EstimateRequested':
        return 'Škola chce nejdřív odhad práce';
      case 'EstimateProposed':
        return 'Podpora poslala odhad práce';
      case 'EstimateApproved':
        return 'Odhad práce schválen';
      case 'EstimateRejected':
        return 'Odhad práce odmítnut';
      case 'TimeCategoryChanged':
        return 'Podpora změnila kategorii času';
      case 'TimeDisputed':
        return 'Kategorie času rozporována';
      case 'TimeDisputeDecided':
        return 'Podpora rozhodla o rozporování času';
      case 'AccessRequested':
        return 'Žádost o vstup do školy';
      case 'AccessGranted':
        return 'Vstup do školy schválen';
      case 'AccessRejected':
        return 'Vstup do školy zamítnut';
      case 'AccessRevoked':
        return 'Vstup do školy odvolán';
      case 'SupportAsChanged':
        return 'Změněno, za koho smí podpora vstoupit';
      case 'SessionStarted':
        return 'Vstup do školy';
      case 'Visit':
        return 'Odchod ze školy';
      case 'SessionEnded':
        return 'Vstup do školy ukončen';
      case 'SessionExpired':
        return 'Vstup do školy vypršel';
      case 'Reset':
        return entry.sessionId
          ? 'Změny tohoto vstupu resetovány'
          : 'Změny podpory resetovány';
      default:
        return '';
    }
  }

  protected isExpanded(visit: TicketVisit): boolean {
    return (
      this.folds().get(visit.sessionId) ??
      (visit.end === 'running' || visit.sessionId === this.lastVisit())
    );
  }

  protected toggleVisit(visit: TicketVisit): void {
    const folds = new Map(this.folds());
    folds.set(visit.sessionId, !this.isExpanded(visit));
    this.folds.set(folds);
  }

  protected visitEntries(visit: TicketVisit): SupportTicketEntry[] {
    return this.isExpanded(visit)
      ? visit.entries
      : visit.entries.filter((e) => !hiddenWhenFolded(e));
  }

  protected visitTime(visit: TicketVisit): string {
    return formatMinutes(visitMinutes(visit, this.now()));
  }

  protected visitEndLabel(visit: TicketVisit): string | null {
    switch (visit.end) {
      case 'running': {
        return visit.inactiveSince
          ? `neaktivní od ${formatDate(visit.inactiveSince, 'time')}`
          : 'probíhá';
      }
      case 'ended':
        return 'ukončen';
      case 'expired':
        return 'vypršel';
      default:
        return null;
    }
  }

  protected hasEntryActions(entry: SupportTicketEntry): boolean {
    return !!entry.canEdit && !this.readOnly() && this.ticketOpen();
  }

  protected openEntryMenu(
    event: Event,
    entry: SupportTicketEntry,
    menu: Menu,
  ): void {
    this.entryMenuItems.set([
      {
        label: 'Upravit',
        icon: 'pi pi-pencil',
        command: () => this.startEdit(entry),
      },
      {
        label: 'Smazat',
        icon: 'pi pi-trash',
        command: () => this.deleteEntry(entry),
      },
    ]);
    menu.toggle(event);
  }

  private startEdit(entry: SupportTicketEntry): void {
    this.editText = entry.text ?? '';
    this.editing.set(entry.id);
  }

  protected saveEdit(entry: SupportTicketEntry): void {
    const ticket = this.ticket();
    const text = this.editText.trim();
    if (!ticket || !text) {
      return;
    }
    this.run(
      this.api.editTicketEntry(ticket.id, entry.id, text),
      () => this.editing.set(null),
      false,
    );
  }

  private deleteEntry(entry: SupportTicketEntry): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.confirmation.confirm({
      header: 'Smazat zprávu',
      message:
        'Zpráva zmizí. Podpora a kolegové se stejnou rolí uvidí, že tu byla.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      acceptButtonProps: { severity: 'danger' },
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.api.deleteTicketEntry(ticket.id, entry.id),
          () => this.notify.success('Zpráva smazána.'),
          false,
        ),
    });
  }

  protected isText(entry: SupportTicketEntry): boolean {
    return entry.kind === 'Message';
  }

  /** The text under a system entry. */
  protected entryText(entry: SupportTicketEntry): string | null {
    return entry.kind === 'HandedOver' || entry.kind === 'Assigned'
      ? null
      : entry.text;
  }

  protected sendByKey(form: NgForm): void {
    if (form.valid && !this.busy()) {
      this.send();
    }
  }

  protected send(): void {
    const ticket = this.ticket();
    const text = this.text.trim();
    if (!ticket || !text) {
      return;
    }
    this.run(this.api.addSchoolTicketMessage(ticket.id, text), () => {
      this.text = '';
    });
  }

  protected openCancel(): void {
    this.cancelReason = '';
    this.cancelReset = false;
    this.resetPreview.set(null);
    this.cancelling.set(true);
  }

  protected setCancelReset(reset: boolean): void {
    this.cancelReset = reset;
    if (reset && !this.resetPreview()) {
      this.loadResetPreview(null);
    }
  }

  protected cancel(): void {
    this.act(
      (id) =>
        this.api.cancelTicket(id, this.cancelReason.trim(), this.cancelReset),
      'Požadavek zrušen.',
      () => this.cancelling.set(false),
    );
  }

  /** A visit runs now — resetting ends it. */
  protected readonly visitRunning = computed(() =>
    this.items().some((i) => i.visit?.end === 'running'),
  );

  protected openReset(sessionId: number | null): void {
    this.resetReason = '';
    this.resetPreview.set(null);
    this.resetting.set({ sessionId });
    this.loadResetPreview(sessionId);
  }

  private loadResetPreview(sessionId: number | null): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.api.previewReset(ticket.id, sessionId).subscribe({
      next: (preview) => this.resetPreview.set(preview),
      error: (err) => {
        this.resetting.set(null);
        this.cancelReset = false;
        this.notify.actionFailed('previewReset failed', err);
      },
    });
  }

  protected reset(): void {
    const sessionId = this.resetting()?.sessionId ?? null;
    this.act(
      (id) => this.api.resetChanges(id, sessionId, this.resetReason.trim()),
      'Změny podpory resetovány.',
      () => this.resetting.set(null),
    );
  }

  protected resetActionLabel(item: SupportResetItem): string {
    return SUPPORT_RESET_ACTION_LABELS[item.action];
  }

  protected close(): void {
    this.act((id) => this.api.closeTicket(id), 'Požadavek uzavřen.');
  }

  protected openReturn(): void {
    this.returnText = '';
    this.returning.set(true);
  }

  protected returnToSupport(): void {
    this.act(
      (id) => this.api.returnTicket(id, this.returnText.trim()),
      'Požadavek vrácen k řešení.',
      () => this.returning.set(false),
    );
  }

  protected reopen(): void {
    this.act((id) => this.api.reopenTicket(id), 'Požadavek znovu otevřen.');
  }

  /** The time or access section changed the ticket (an estimate decided, a key approved…). */
  protected timeChanged(detail: SupportTicketDetail): void {
    this.detail.set(detail);
    this.changed.emit(detail.ticket);
  }

  private act(
    call: (id: number) => Observable<SupportTicketDetail>,
    success?: string,
    then?: () => void,
  ): void {
    const ticket = this.ticket();
    if (ticket) {
      this.run(call(ticket.id), () => {
        then?.();
        if (success) {
          this.notify.success(success);
        }
      });
    }
  }

  /** `scroll`: to the newest — not after changing an older entry. */
  private run(
    call: Observable<SupportTicketDetail>,
    then: () => void,
    scroll = true,
  ): void {
    this.busy.set(true);
    call.subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.detail.set(detail);
        if (scroll) {
          this.scrollToNewest();
        }
        this.changed.emit(detail.ticket);
        then();
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('support ticket action failed', err);
      },
    });
  }
}
