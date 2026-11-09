import {
  Component,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  SUPPORT_TICKET_STATUS_LABELS,
  SchoolSupport,
  SupportAccess,
  SupportApiService,
  SupportTicket,
  SupportTicketStatus,
  approvalDuration,
} from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { highlightElement, onLinkedItem } from '../highlight';
import { InfoHint } from '../info-hint/info-hint';
import { Loading } from '../loading/loading';
import { MarkdownEditor } from '../markdown/markdown-editor';
import { NotifyService } from '../notify.service';
import { SupportTicketDialog } from '../support-ticket/support-ticket-dialog';
import { ticketStatusSeverity } from '../support-ticket/ticket-status';

/** The school's support tickets (asking the SuperAdmins for help, talked
 * over in each ticket's thread, entering allowed per ticket) and whether
 * the support may enter the school at all. `?ticket=<id>` (a
 * notification's link) opens one. */
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
    Select,
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

  protected readonly accessOptions: { value: SupportAccess; label: string }[] =
    [
      { value: 'Approval', label: 'Po schválení' },
      { value: 'Always', label: 'Kdykoliv' },
      { value: 'Never', label: 'Nikdy' },
    ];

  protected readonly support = signal<SchoolSupport | null>(null);
  protected readonly access = signal<SupportAccess>('Approval');
  protected readonly duration = computed(() =>
    approvalDuration(this.support()?.approvalMinutes ?? 24 * 60),
  );
  protected readonly tickets = signal<SupportTicket[] | null>(null);
  protected readonly ticketDialogVisible = signal(false);
  protected ticketTitle = '';
  protected ticketDescription = '';
  protected allowAccess = true;

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
        this.tickets.set(tickets);
        if (highlight) {
          highlightElement(`support-ticket-${highlight}`);
        }
      },
      error: (err) => {
        console.error('getSchoolTickets failed', err);
        this.tickets.update((t) => t ?? []);
      },
    });
  }

  protected save(): void {
    this.api.setSupportAccess(this.access()).subscribe({
      next: (support) => {
        this.apply(support);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setSupportAccess failed', err),
    });
  }

  protected openTicketDialog(): void {
    this.ticketTitle = '';
    this.ticketDescription = '';
    this.allowAccess = true;
    this.notify.clear();
    this.ticketDialogVisible.set(true);
  }

  protected createTicket(): void {
    this.api
      .createTicket(
        this.ticketTitle.trim(),
        this.ticketDescription.trim(),
        this.access() === 'Approval' && this.allowAccess,
      )
      .subscribe({
        next: (detail) => {
          this.tickets.update((tickets) => [detail.ticket, ...(tickets ?? [])]);
          this.ticketDialogVisible.set(false);
          this.notify.success('Požadavek odeslán podpoře.');
        },
        error: (err) => this.notify.actionFailed('createTicket failed', err),
      });
  }

  protected changed(ticket: SupportTicket): void {
    this.tickets.update((tickets) =>
      (tickets ?? []).map((t) => (t.id === ticket.id ? ticket : t)),
    );
  }

  protected statusLabel(status: SupportTicketStatus): string {
    return SUPPORT_TICKET_STATUS_LABELS[status];
  }

  protected readonly severity = ticketStatusSeverity;

  private apply(support: SchoolSupport): void {
    this.support.set(support);
    this.access.set(support.access);
  }
}
