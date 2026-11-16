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
import { FormsModule } from '@angular/forms';
import { ConfirmationService, MenuItem } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { Menu } from '@openng/optimus-ui/menu';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import {
  AuthApiService,
  DateFormatPipe,
  SUPPORT_TICKET_ENTRY_CHANGE_LABELS,
  SUPPORT_TICKET_STATUS_LABELS,
  SupportApiService,
  SupportTicket,
  SupportTicketDetail,
  SupportTicketEntry,
  SupportTicketEntryRevision,
  SupportTicketStatus,
  SuperAdmin,
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
import { FORM_FIELDS, FloatField } from '../forms';
import { Markdown } from '../markdown/markdown';
import { MarkdownEditor } from '../markdown/markdown-editor';
import { NotifyService } from '../notify.service';
import { schoolUrl } from '../school-context';
import { ticketStatusSeverity } from './ticket-status';
import {
  TicketVisit,
  formatMinutes,
  groupTicketThread,
  hiddenWhenFolded,
  idleSince,
  visitMinutes,
} from './ticket-thread';

/** A support ticket with its thread — for the SuperAdmins (`side`
 * "support": taking it over, its state, asking to enter and entering,
 * internal notes), a SuperAdmin inside the school ("session": the visit's
 * ticket, only writing to it) or the school's admins ("school": answering,
 * allowing entering, cancelling; no internal entries). */
@Component({
  selector: 'app-support-ticket-dialog',
  imports: [
    Button,
    DateFormatPipe,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Markdown,
    MarkdownEditor,
    NgTemplateOutlet,
    Menu,
    Select,
    Tag,
    ToggleSwitch,
    Tooltip,
  ],
  templateUrl: './support-ticket-dialog.html',
})
export class SupportTicketDialog {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly myId = inject(AuthApiService).getUserId();

  readonly side = input.required<'support' | 'session' | 'school'>();
  protected readonly bySupport = computed(() => this.side() !== 'school');
  /** The school's side in support mode: looking only. */
  readonly readOnly = input(false);
  /** The ticket as it is after a change — for the list. */
  readonly changed = output<SupportTicket>();

  protected readonly detail = signal<SupportTicketDetail | null>(null);
  protected readonly visible = signal(false);
  protected readonly busy = signal(false);
  protected text = '';
  protected internal = false;
  protected readonly releasing = signal(false);
  protected summary = '';
  protected readonly cancelling = signal(false);
  protected cancelReason = '';
  protected readonly returning = signal(false);
  protected returnText = '';
  /** The message or note being changed in place. */
  protected readonly editing = signal<number | null>(null);
  protected editText = '';
  protected readonly entryMenuItems = signal<MenuItem[]>([]);
  /** Messages with their list of changes shown. */
  protected readonly changesShown = signal(new Set<number>());
  /** Who "Převzít" gives it to: me (null) or, "Předat", another SuperAdmin. */
  protected assignee: number | null = null;
  protected readonly superAdmins = signal<SuperAdmin[]>([]);
  protected readonly assigneeOptions = computed(() => [
    { value: null, label: 'Já' },
    ...this.superAdmins()
      .filter((a) => a.id !== this.myId)
      .map((a) => ({ value: a.id as number | null, label: a.name })),
  ]);
  /** The SuperAdmins' "Volání API": each change's request under it. */
  protected readonly showRequests = signal(readShowRequests());
  protected readonly hasRequests = computed(
    () => !!this.detail()?.entries.some((e) => e.request),
  );
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
  /** Reloads keep a running visit's time current. */
  private readonly now = computed(() => (this.detail(), Date.now()));
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
  private polling?: Subscription;
  private expiry?: ReturnType<typeof setTimeout>;

  constructor() {
    inject(DestroyRef).onDestroy(() => this.stopRefreshing());
  }

  protected readonly ticket = computed(() => this.detail()?.ticket ?? null);
  protected readonly ticketOpen = computed(() => {
    const ticket = this.ticket();
    return !!ticket && isTicketOpen(ticket.status);
  });
  /** The ticket's own controls (state, taking over, cancelling) — not
   * from inside the school, not when only looking. */
  protected readonly canManage = computed(
    () => this.ticketOpen() && !this.readOnly() && this.side() !== 'session',
  );
  protected readonly mine = computed(
    () => this.ticket()?.assigneeUserId === this.myId,
  );
  protected readonly statusActions = computed(() =>
    STATUS_ACTIONS.filter((a) => a.status !== this.ticket()?.status),
  );
  /** The last day reopening is possible (it ends at its midnight). */
  protected readonly reopenLastDay = computed(() => {
    const until = this.ticket()?.reopenableUntil;
    return until ? new Date(new Date(until).getTime() - 1).toISOString() : null;
  });

  /** Entering allowed right now — by the school's policy or the ticket. */

  protected readonly canEnter = computed(() => {
    const ticket = this.ticket();
    return (
      !!ticket &&
      (ticket.schoolAccess === 'Always' ||
        (ticket.schoolAccess === 'Approval' && !!ticket.accessUntil))
    );
  });

  /** `id` of the ticket — not with "session", which has its own. */
  open(id = 0): void {
    this.text = '';
    this.internal = false;
    this.assignee = null;
    this.folds.set(new Map());
    this.notify.clear();
    if (this.side() === 'support' && this.superAdmins().length === 0) {
      this.api.getSuperAdmins().subscribe({
        next: (admins) => this.superAdmins.set(admins),
        error: () => this.superAdmins.set([]),
      });
    }
    this.load(id).subscribe({
      next: (detail) => {
        this.show(detail);
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

  private load(id: number): Observable<SupportTicketDetail> {
    switch (this.side()) {
      case 'support':
        return this.api.getTicket(id);
      case 'session':
        return this.api.getSessionTicket();
      default:
        return this.api.getSchoolTicket(id);
    }
  }

  /** The other side changes the ticket too (allows entering, takes it
   * back, answers…) and allowing runs out: while open, it reloads now and
   * then, and when entering stops being allowed. */
  private startRefreshing(): void {
    this.polling?.unsubscribe();
    // Also right when the tab is back in front — e.g. from the school's.
    this.polling = merge(
      interval(15_000),
      fromEvent(document, 'visibilitychange'),
    )
      .pipe(filter(() => document.visibilityState === 'visible'))
      .subscribe(() => this.reload());
  }

  private stopRefreshing(): void {
    this.polling?.unsubscribe();
    this.polling = undefined;
    clearTimeout(this.expiry);
  }

  private reload(): void {
    const ticket = this.ticket();
    if (!ticket || this.busy() || !this.visible()) {
      return;
    }
    this.load(ticket.id).subscribe({
      next: (detail) => {
        if (this.visible() && !this.busy()) {
          const atNewest = this.atNewest();
          this.show(detail);
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

  private show(detail: SupportTicketDetail): void {
    this.detail.set(detail);
    clearTimeout(this.expiry);
    const until = detail.ticket.accessUntil;
    if (until) {
      const wait = new Date(until).getTime() - Date.now() + 1000;
      // setTimeout overflows past ~24.8 days.
      if (wait < 2 ** 31 - 1) {
        this.expiry = setTimeout(() => this.reload(), Math.max(wait, 0));
      }
    }
  }

  protected setShowRequests(show: boolean): void {
    this.showRequests.set(show);
    try {
      localStorage.setItem(SHOW_REQUESTS_KEY, show ? '1' : '0');
    } catch {
      // Only a convenience.
    }
  }

  protected statusLabel(status: SupportTicketStatus): string {
    return SUPPORT_TICKET_STATUS_LABELS[status];
  }

  protected readonly severity = ticketStatusSeverity;

  /** The line a system entry shows. */
  protected entryLabel(entry: SupportTicketEntry): string {
    switch (entry.kind) {
      case 'Created':
        return 'Požadavek založen';
      case 'Assigned':
        return entry.text ? `Převzato od: ${entry.text}` : 'Převzato';
      case 'Released':
        return 'Práce přerušena — vráceno k převzetí';
      case 'HandedOver':
        return `Předáno: ${entry.text ?? ''}`;
      case 'StatusChanged':
        return `Stav: ${entry.status ? SUPPORT_TICKET_STATUS_LABELS[entry.status] : ''}`;
      case 'AccessRequested':
        return 'Žádost o vstup do školy';
      case 'AccessGranted':
        return 'Vstup do školy povolen';
      case 'AccessRevoked':
        return 'Povolení vstupu odebráno';
      case 'SessionStarted':
        return 'Vstup do školy';
      case 'Visit':
        return 'Odchod ze školy';
      case 'SessionEnded':
        return 'Vstup do školy ukončen';
      case 'SessionExpired':
        return 'Vstup do školy vypršel';
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
        const idle = idleSince(visit, this.now());
        return idle ? `bez aktivity od ${formatDate(idle, 'time')}` : 'probíhá';
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
    return !!entry.canEdit && !this.readOnly();
  }

  protected openEntryMenu(
    event: Event,
    entry: SupportTicketEntry,
    menu: Menu,
  ): void {
    const open = this.ticketOpen();
    this.entryMenuItems.set([
      ...(entry.internal || open
        ? [
            {
              label: 'Upravit',
              icon: 'pi pi-pencil',
              command: () => this.startEdit(entry),
            },
          ]
        : []),
      ...(entry.bySupport && (!entry.internal || open)
        ? [
            {
              label: entry.internal ? 'Poslat škole' : 'Změnit na interní',
              icon: entry.internal ? 'pi pi-send' : 'pi pi-lock',
              command: () => this.retype(entry),
            },
          ]
        : []),
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
      this.api.editTicketEntry(this.side(), ticket.id, entry.id, text),
      () => this.editing.set(null),
      false,
    );
  }

  private deleteEntry(entry: SupportTicketEntry): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    const note = entry.internal;
    this.confirmation.confirm({
      header: note ? 'Smazat poznámku' : 'Smazat zprávu',
      message: note
        ? 'Poznámka zmizí, superadmini v jejích změnách uvidí, co v ní stálo.'
        : entry.bySupport
          ? 'Škola zprávu přestane vidět a zmizí i upozornění na ni. Co už odešlo e-mailem nebo do telefonu, se vzít zpět nedá. Superadmini v jejích změnách uvidí, co v ní stálo.'
          : 'Zpráva zmizí. Podpora a kolegové se stejnou rolí v jejích změnách uvidí, co v ní stálo.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      acceptButtonProps: { severity: 'danger' },
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.api.deleteTicketEntry(this.side(), ticket.id, entry.id),
          () =>
            this.notify.success(note ? 'Poznámka smazána.' : 'Zpráva smazána.'),
          false,
        ),
    });
  }

  protected toggleChanges(entry: SupportTicketEntry): void {
    this.changesShown.update((shown) => {
      const next = new Set(shown);
      if (!next.delete(entry.id)) {
        next.add(entry.id);
      }
      return next;
    });
  }

  protected changeLabel(revision: SupportTicketEntryRevision): string {
    return SUPPORT_TICKET_ENTRY_CHANGE_LABELS[revision.change];
  }

  /** The text a change replaced or removed — the others keep it. */
  protected changeText(revision: SupportTicketEntryRevision): string | null {
    return revision.change === 'Edited' || revision.change === 'Deleted'
      ? revision.text
      : null;
  }

  private retype(entry: SupportTicketEntry): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    const internal = !entry.internal;
    this.confirmation.confirm({
      header: internal ? 'Změnit na interní poznámku' : 'Poslat škole',
      message: internal
        ? 'Škola zprávu přestane vidět a zmizí i upozornění na ni. Co už odešlo e-mailem nebo do telefonu, se vzít zpět nedá.'
        : 'Poznámka se stane zprávou škole: škola ji uvidí a dostane upozornění.',
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: internal ? 'Změnit na interní' : 'Poslat škole',
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.run(
          this.side() === 'session'
            ? this.api.setSessionEntryInternal(entry.id, internal)
            : this.api.setTicketEntryInternal(ticket.id, entry.id, internal),
          () =>
            this.notify.success(
              internal ? 'Změněno na interní poznámku.' : 'Posláno škole.',
            ),
          false,
        ),
    });
  }

  protected isText(entry: SupportTicketEntry): boolean {
    return entry.kind === 'Message' || entry.kind === 'Note';
  }

  /** The text under a system entry — taking and handing over name the
   * other SuperAdmin in their label already. */
  protected entryText(entry: SupportTicketEntry): string | null {
    return entry.kind === 'HandedOver' || entry.kind === 'Assigned'
      ? null
      : entry.text;
  }

  protected send(): void {
    const ticket = this.ticket();
    const text = this.text.trim();
    if (!ticket || !text) {
      return;
    }
    const internal = this.internal;
    this.run(
      this.side() === 'support'
        ? this.api.addTicketMessage(ticket.id, text, internal)
        : this.side() === 'session'
          ? this.api.addSessionTicketMessage(text, internal)
          : this.api.addSchoolTicketMessage(ticket.id, text),
      () => {
        this.text = '';
      },
    );
  }

  protected assign(): void {
    const to = this.assignee;
    this.act(
      (id) => this.api.assignTicket(id, to ?? undefined),
      to ? 'Požadavek předán.' : undefined,
      () => (this.assignee = null),
    );
  }

  /** From another SuperAdmin taking care of it. */
  protected takeOver(): void {
    const ticket = this.ticket();
    const from = ticket?.assigneeUserId;
    if (!ticket || !from) {
      return;
    }
    this.confirmation.confirm({
      header: `Převzít od ${ticket.assigneeName}`,
      message: `Požadavek teď řeší ${ticket.assigneeName} a může na něm právě pracovat. Převzetím o něj přijde: pokud je zrovna ve škole, jeho vstup hned skončí a neuložená práce se ztratí. Dostane o tom upozornění, škola uvidí nového řešitele.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Převzít',
      acceptButtonProps: { severity: 'danger' },
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.act(
          (id) => this.api.takeOverTicket(id, from),
          'Požadavek převzat.',
        ),
    });
  }

  protected setStatus(status: SupportTicketStatus): void {
    const set = () =>
      this.act((id) => this.api.setTicketStatus(id, status), 'Stav změněn.');
    if (status !== 'Resolved') {
      set();
      return;
    }
    this.confirmation.confirm({
      header: 'Vyřešeno',
      message:
        'Vstup do školy kvůli požadavku skončí. Škola dostane upozornění a vyřešení potvrdí, nebo požadavek vrátí k řešení; bez reakce se za 30 dní uzavře sám.',
      icon: 'pi pi-check-circle',
      acceptLabel: 'Vyřešeno',
      rejectLabel: 'Zpět',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: set,
    });
  }

  protected openRelease(): void {
    this.summary = '';
    this.releasing.set(true);
  }

  protected release(): void {
    this.act(
      (id) => this.api.releaseTicket(id, this.summary.trim()),
      'Požadavek vrácen k převzetí.',
      () => this.releasing.set(false),
    );
  }

  protected requestAccess(): void {
    this.act(
      (id) => this.api.requestTicketAccess(id),
      'Žádost o vstup odeslána škole.',
    );
  }

  protected grantAccess(): void {
    this.act((id) => this.api.grantTicketAccess(id), 'Vstup povolen.');
  }

  protected revokeAccess(): void {
    this.act((id) => this.api.revokeTicketAccess(id), 'Povolení odebráno.');
  }

  protected openCancel(): void {
    this.cancelReason = '';
    this.cancelling.set(true);
  }

  protected cancel(): void {
    this.act(
      (id) => this.api.cancelTicket(id, this.cancelReason.trim()),
      'Požadavek zrušen.',
      () => this.cancelling.set(false),
    );
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
      (id) =>
        this.bySupport()
          ? this.api.returnTicketBySupport(id, this.returnText.trim())
          : this.api.returnTicket(id, this.returnText.trim()),
      'Požadavek vrácen k řešení.',
      () => this.returning.set(false),
    );
  }

  protected reopen(): void {
    this.act((id) => this.api.reopenTicket(id), 'Požadavek znovu otevřen.');
  }

  /** An audited, 2-hour Admin session in the school (support mode); the own
   * SuperAdmin session comes back on "Opustit", here at the ticket. */
  protected enter(): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    // The school opens in a new tab, this one stays at the ticket. Opened
    // right on the click: after the request it'd be blocked as a pop-up.
    const tab = window.open('', '_blank');
    this.busy.set(true);
    this.api.enterForTicket(ticket.id).subscribe({
      next: (session) => {
        // The school's page (its own subdomain in production). The one-time
        // code rides in the fragment, which never reaches any server log;
        // `back` is for a tab that can't close itself on leaving.
        const back = `superadmin/support?ticket=${ticket.id}`;
        const url = `${schoolUrl(session.schoolSlug, 'support/enter')}#code=${session.code}&back=${encodeURIComponent(back)}`;
        if (tab) {
          tab.location.href = url;
          this.busy.set(false);
          this.reload();
        } else {
          window.location.href = url;
        }
      },
      error: (err) => {
        tab?.close();
        this.busy.set(false);
        this.notify.actionFailed('enterForTicket failed', err);
        this.open(ticket.id);
      },
    });
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
        this.show(detail);
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

const SHOW_REQUESTS_KEY = 'supportTicket.showRequests';

function readShowRequests(): boolean {
  try {
    return localStorage.getItem(SHOW_REQUESTS_KEY) === '1';
  } catch {
    return false;
  }
}

/** What the assignee may set — school's answer returns it to Assigned. */
const STATUS_ACTIONS: {
  status: SupportTicketStatus;
  label: string;
  icon: string;
}[] = [
  { status: 'InProgress', label: 'Řeším', icon: 'pi pi-play' },
  { status: 'WaitingForSchool', label: 'Čekám na školu', icon: 'pi pi-clock' },
  { status: 'Resolved', label: 'Vyřešeno', icon: 'pi pi-check' },
];
