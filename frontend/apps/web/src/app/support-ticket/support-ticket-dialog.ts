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
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import {
  AuthApiService,
  DateFormatPipe,
  SUPPORT_TICKET_STATUS_LABELS,
  SupportApiService,
  SupportTicket,
  SupportTicketDetail,
  SupportTicketEntry,
  SupportTicketStatus,
  isTicketOpen,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { FORM_FIELDS, FloatField } from '../forms';
import { Markdown } from '../markdown/markdown';
import { MarkdownEditor } from '../markdown/markdown-editor';
import { NotifyService } from '../notify.service';
import { schoolUrl } from '../school-context';
import { ticketStatusSeverity } from './ticket-status';

/** A support ticket with its thread — for the SuperAdmins (`side`
 * "support": taking it over, its state, asking to enter and entering,
 * internal notes) or the school's admins ("school": answering, allowing
 * entering, cancelling; no internal entries). */
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
    Select,
    Tag,
    ToggleSwitch,
  ],
  templateUrl: './support-ticket-dialog.html',
})
export class SupportTicketDialog {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly myId = inject(AuthApiService).getUserId();

  readonly side = input.required<'support' | 'school'>();
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

  protected readonly ticket = computed(() => this.detail()?.ticket ?? null);
  protected readonly ticketOpen = computed(() => {
    const ticket = this.ticket();
    return !!ticket && isTicketOpen(ticket.status);
  });
  protected readonly mine = computed(
    () => this.ticket()?.assigneeUserId === this.myId,
  );
  protected readonly statusOptions: {
    value: SupportTicketStatus;
    label: string;
  }[] = (
    ['Assigned', 'InProgress', 'WaitingForSchool', 'Resolved'] as const
  ).map((value) => ({ value, label: SUPPORT_TICKET_STATUS_LABELS[value] }));
  /** Entering allowed right now — by the school's policy or the ticket. */
  protected readonly canEnter = computed(() => {
    const ticket = this.ticket();
    return (
      !!ticket &&
      (ticket.schoolAccess === 'Always' ||
        (ticket.schoolAccess === 'Approval' && !!ticket.accessUntil))
    );
  });

  open(id: number): void {
    this.text = '';
    this.internal = false;
    this.notify.clear();
    const load =
      this.side() === 'support'
        ? this.api.getTicket(id)
        : this.api.getSchoolTicket(id);
    load.subscribe({
      next: (detail) => {
        this.detail.set(detail);
        this.visible.set(true);
      },
      error: (err) => this.notify.actionFailed('getTicket failed', err),
    });
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
        return 'Převzato';
      case 'Released':
        return 'Práce přerušena — vráceno k převzetí';
      case 'StatusChanged':
        return `Stav: ${entry.status ? SUPPORT_TICKET_STATUS_LABELS[entry.status] : ''}`;
      case 'AccessRequested':
        return 'Žádost o vstup do školy';
      case 'AccessGranted':
        return 'Vstup do školy povolen';
      case 'AccessRevoked':
        return 'Povolení vstupu odebráno';
      case 'Visit':
        return 'Vstup do školy';
      default:
        return '';
    }
  }

  protected isText(entry: SupportTicketEntry): boolean {
    return entry.kind === 'Message' || entry.kind === 'Note';
  }

  protected send(): void {
    const ticket = this.ticket();
    const text = this.text.trim();
    if (!ticket || !text) {
      return;
    }
    this.run(
      this.side() === 'support'
        ? this.api.addTicketMessage(ticket.id, text, this.internal)
        : this.api.addSchoolTicketMessage(ticket.id, text),
      () => {
        this.text = '';
      },
    );
  }

  protected assign(): void {
    this.act((id) => this.api.assignTicket(id));
  }

  protected setStatus(status: SupportTicketStatus): void {
    this.act((id) => this.api.setTicketStatus(id, status), 'Stav změněn.');
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

  protected cancel(): void {
    this.act((id) => this.api.cancelTicket(id), 'Požadavek zrušen.');
  }

  /** An audited, 2-hour Admin session in the school (support mode); the own
   * SuperAdmin session comes back on "Opustit", here at the ticket. */
  protected enter(): void {
    const ticket = this.ticket();
    if (!ticket) {
      return;
    }
    this.busy.set(true);
    this.api.enterForTicket(ticket.id).subscribe({
      next: (session) => {
        // A full page load onto the school's page (its own subdomain in
        // production). The one-time code rides in the fragment, which never
        // reaches any server log.
        const back = `superadmin/support?ticket=${ticket.id}`;
        window.location.href = `${schoolUrl(session.schoolSlug, 'support/enter')}#code=${session.code}&back=${encodeURIComponent(back)}`;
      },
      error: (err) => {
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

  private run(call: Observable<SupportTicketDetail>, then: () => void): void {
    this.busy.set(true);
    call.subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.detail.set(detail);
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
