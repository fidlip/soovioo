import {
  Component,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  SUPPORT_ACCESS_LABELS,
  SUPPORT_TICKET_STATUS_LABELS,
  SchoolSupport,
  SupportAccess,
  SupportApiService,
  SupportTicket,
  SupportTicketStatus,
  SupportTicketSummary,
} from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { highlightElement, onLinkedItem } from '../highlight';
import { InfoHint } from '@fidlip/soovioo-ui';
import { Loading } from '@fidlip/soovioo-ui';
import { MarkdownEditor } from '@fidlip/soovioo-ui';
import { NotifyService } from '@fidlip/soovioo-ui';
import { SupportTicketDialog } from '../support-ticket/support-ticket-dialog';
import { ticketStatusSeverity } from '../support-ticket/ticket-status';

/** The school's support tickets, kept in the backoffice (asking the support
 * for help, talked over in each ticket's thread); whether the support may
 * enter the school at all is set in Aplikace → Nastavení
 * (SupportAccessSettings). `?ticket=<id>` (a notification's link) opens one. */
@Component({
  selector: 'app-school-support',
  imports: [
    Button,
    Card,
    Checkbox,
    DateFormatPipe,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    InfoHint,
    InputText,
    Loading,
    MarkdownEditor,
    RouterLink,
    SupportTicketDialog,
    TableModule,
    Tag,
  ],
  templateUrl: './school-support.html',
})
export class SchoolSupportSection {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly detail = viewChild.required(SupportTicketDialog);
  private readonly injector = inject(Injector);

  protected readonly support = signal<SchoolSupport | null>(null);
  protected readonly tickets = signal<SupportTicketSummary[] | null>(null);
  /** The backoffice can't be reached: nothing to show nor send now. */
  protected readonly unavailable = signal(false);
  protected readonly ticketDialogVisible = signal(false);
  protected readonly sending = signal(false);
  protected ticketTitle = '';
  protected ticketDescription = '';
  protected estimateRequested = false;

  /** In support mode the SuperAdmin can only look — the school decides. */
  protected readonly readOnly =
    inject(AuthApiService).getSupportInfo() !== null;

  constructor() {
    this.reload();
    onLinkedItem(
      'ticket',
      (id) => {
        this.reload(id);
        // A notification's link comes before the view is there.
        afterNextRender(() => this.detail().open(id), {
          injector: this.injector,
        });
      },
      ['group', 'section'],
    );
    this.api.getSchoolSupport().subscribe({
      next: (support) => this.apply(support),
      error: (err) => console.error('getSchoolSupport failed', err),
    });
  }

  private reload(highlight?: number): void {
    this.api.getSchoolTickets().subscribe({
      next: (tickets) => {
        this.unavailable.set(false);
        this.tickets.set(tickets);
        if (highlight) {
          highlightElement(`support-ticket-${highlight}`);
        }
      },
      error: (err) => {
        console.error('getSchoolTickets failed', err);
        this.unavailable.set(true);
        this.tickets.update((t) => t ?? []);
      },
    });
  }

  protected accessLabel(access: SupportAccess): string {
    return SUPPORT_ACCESS_LABELS[access];
  }

  /** A draft not sent stays for the next try. */
  protected openTicketDialog(): void {
    this.notify.clear();
    this.ticketDialogVisible.set(true);
  }

  protected createTicket(): void {
    this.sending.set(true);
    this.api
      .createTicket(
        this.ticketTitle.trim(),
        this.ticketDescription.trim(),
        this.estimateRequested,
      )
      .subscribe({
        next: () => {
          this.sending.set(false);
          this.ticketTitle = '';
          this.ticketDescription = '';
          this.estimateRequested = false;
          this.ticketDialogVisible.set(false);
          this.notify.success('Požadavek odeslán podpoře.');
          this.reload();
        },
        error: (err) => {
          this.sending.set(false);
          this.notify.actionFailed('createTicket failed', err);
        },
      });
  }

  protected changed(ticket: SupportTicket): void {
    this.tickets.update((tickets) =>
      (tickets ?? []).map((t) =>
        t.id === ticket.id
          ? {
              ...t,
              title: ticket.title,
              status: ticket.status,
              assigneeName: ticket.assigneeName,
              overdue: ticket.overdue,
            }
          : t,
      ),
    );
  }

  protected statusLabel(status: SupportTicketStatus): string {
    return SUPPORT_TICKET_STATUS_LABELS[status];
  }

  protected readonly severity = ticketStatusSeverity;

  private apply(support: SchoolSupport): void {
    this.support.set(support);
  }
}
