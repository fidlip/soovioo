import {
  Component,
  Injector,
  afterNextRender,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import { SelectButton } from '@openng/optimus-ui/selectbutton';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  SUPPORT_TICKET_STATUS_LABELS,
  SchoolsApiService,
  SupportApiService,
  SupportTicket,
  SupportTicketFilter,
  SupportTicketStatus,
} from '@soovioo/shared';
import { Subscription, forkJoin } from 'rxjs';
import { FORM_FIELDS, FloatField } from '../forms';
import { highlightElement, onLinkedItem } from '../highlight';
import { Loading } from '../loading/loading';
import { MarkdownEditor } from '../markdown/markdown-editor';
import { NotifyService } from '../notify.service';
import { SupportTicketDialog } from '../support-ticket/support-ticket-dialog';
import { ticketStatusSeverity } from '../support-ticket/ticket-status';

/** SuperAdmin: the schools' support tickets — mine, not taken over, open
 * (by due date) or all, optionally of one school; a ticket opens with its
 * thread. `?ticket=<id>` (a notification's link, the way back from the
 * school) opens one, `?school=<id>` lists one school's. */
@Component({
  selector: 'app-support-tickets-page',
  imports: [
    Button,
    Card,
    DateFormatPipe,
    Dialog,
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    InputText,
    Loading,
    MarkdownEditor,
    Select,
    SelectButton,
    SupportTicketDialog,
    TableModule,
    Tag,
  ],
  templateUrl: './support-tickets-page.html',
})
export class SupportTicketsPage {
  private readonly api = inject(SupportApiService);
  private readonly schoolsApi = inject(SchoolsApiService);
  private readonly notify = inject(NotifyService);
  private readonly detail = viewChild.required(SupportTicketDialog);
  private readonly injector = inject(Injector);

  protected readonly filterOptions: {
    value: SupportTicketFilter;
    label: string;
  }[] = [
    { value: 'Mine', label: 'Moje' },
    { value: 'Unassigned', label: 'Nepřevzaté' },
    { value: 'Open', label: 'Otevřené' },
    { value: 'All', label: 'Vše' },
  ];
  protected filter: SupportTicketFilter = 'Open';
  protected schoolId: number | null = null;
  protected readonly schools = signal<{ id: number; name: string }[]>([]);

  protected readonly tickets = signal<SupportTicket[] | null>(null);
  /** The latest load only — an older answer arriving later would bring back
   * a stale list. */
  private loading?: Subscription;

  protected readonly creating = signal(false);
  protected newSchoolId: number | null = null;
  protected newTitle = '';
  protected newDescription = '';

  constructor() {
    forkJoin([
      this.schoolsApi.getSchools(1, 100),
      this.schoolsApi.getSchools(1, 100, undefined, true),
    ]).subscribe({
      next: ([real, demos]) =>
        this.schools.set(
          [...real.items, ...demos.items]
            .map((s) => ({ id: s.id, name: s.name }))
            .sort((a, b) => a.name.localeCompare(b.name, 'cs')),
        ),
      error: (err) => console.error('getSchools failed', err),
    });
    this.reload();
    // After the plain load: a load cancels the one before it.
    onLinkedItem('school', (id) => {
      this.schoolId = id;
      this.filter = 'All';
      this.reload();
    });
    onLinkedItem('ticket', (id) => this.openTicket(id));
  }

  protected reload(highlight?: number): void {
    this.loading?.unsubscribe();
    this.loading = this.api.getTickets(this.filter, this.schoolId).subscribe({
      next: (tickets) => {
        if (
          highlight &&
          this.filter !== 'All' &&
          !tickets.some((t) => t.id === highlight)
        ) {
          // Not in this view (closed, someone else's) — only in all of them.
          this.filter = 'All';
          this.reload(highlight);
          return;
        }
        this.tickets.set(tickets);
        if (highlight) {
          highlightElement(`support-ticket-${highlight}`);
        }
      },
      error: (err) => {
        console.error('getTickets failed', err);
        this.tickets.update((t) => t ?? []);
      },
    });
  }

  protected openTicket(id: number): void {
    this.reload(id);
    // A notification's link comes before the view is there.
    afterNextRender(() => this.detail().open(id), { injector: this.injector });
  }

  /** A change in the detail: the list follows (also dropping a ticket that
   * no longer belongs to the view on the next load). */
  protected changed(ticket: SupportTicket): void {
    this.tickets.update((tickets) =>
      (tickets ?? []).map((t) => (t.id === ticket.id ? ticket : t)),
    );
  }

  protected statusLabel(status: SupportTicketStatus): string {
    return SUPPORT_TICKET_STATUS_LABELS[status];
  }

  protected readonly severity = ticketStatusSeverity;

  protected openCreate(): void {
    this.newSchoolId = this.schoolId;
    this.newTitle = '';
    this.newDescription = '';
    this.notify.clear();
    this.creating.set(true);
  }

  protected create(): void {
    if (!this.newSchoolId) {
      return;
    }
    this.api
      .createTicketForSchool(
        this.newSchoolId,
        this.newTitle.trim(),
        this.newDescription.trim(),
      )
      .subscribe({
        next: (detail) => {
          this.creating.set(false);
          this.notify.success('Požadavek založen.');
          this.openTicket(detail.ticket.id);
        },
        error: (err) =>
          this.notify.actionFailed('createTicketForSchool failed', err),
      });
  }
}
