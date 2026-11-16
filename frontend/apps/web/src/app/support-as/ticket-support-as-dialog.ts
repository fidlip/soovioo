import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import {
  SupportApiService,
  SupportTicket,
  SupportTicketDetail,
} from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { SupportAsUsersSelect } from './support-as-users-select';

/** The users the support may enter the school as for one ticket — the
 * author, or e.g. the one the problem is about. */
@Component({
  selector: 'app-ticket-support-as-dialog',
  imports: [Button, Dialog, FormsModule, SupportAsUsersSelect],
  templateUrl: './ticket-support-as-dialog.html',
})
export class TicketSupportAsDialog {
  private readonly api = inject(SupportApiService);
  private readonly notify = inject(NotifyService);

  readonly saved = output<SupportTicketDetail>();

  protected readonly visible = signal(false);
  protected readonly busy = signal(false);
  protected userIds: number[] = [];
  private ticketId = 0;

  open(ticket: SupportTicket): void {
    this.ticketId = ticket.id;
    this.userIds = (ticket.supportAsUsers ?? []).map((u) => u.id);
    this.visible.set(true);
  }

  protected save(): void {
    this.busy.set(true);
    this.api.setTicketSupportAsUsers(this.ticketId, this.userIds).subscribe({
      next: (detail) => {
        this.busy.set(false);
        this.visible.set(false);
        this.notify.success('Uloženo.');
        this.saved.emit(detail);
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('setTicketSupportAsUsers failed', err);
      },
    });
  }
}
