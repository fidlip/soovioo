import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { DateFormatPipe, SupportPresence } from '@soovioo/shared';
import { SupportTicketDialog } from '../../support-ticket/support-ticket-dialog';
import { UnreadCountsService } from '../../unread-counts.service';

/** A SuperAdmin in the school as someone ("Vstoupit jako…"), refreshed with
 * the badges: the user they're in as learns it, with what that means; the
 * author of the ticket they came for sees as whom, with the ticket. */
@Component({
  selector: 'app-support-presence',
  imports: [Button, DateFormatPipe, Dialog, SupportTicketDialog],
  templateUrl: './support-presence.html',
  host: { class: 'contents' },
})
export class SupportPresenceBanners {
  protected readonly presences = inject(UnreadCountsService).support;
  protected readonly detail = signal<SupportPresence | null>(null);
  /** Someone is in as another user for the caller's ticket. */
  protected readonly forMyTicket = computed(() =>
    this.presences().some((p) => !p.asMe),
  );
  private readonly ticketDialog =
    viewChild<SupportTicketDialog>('schoolTicketDialog');

  /** The author's ticket, as on Podpora. */
  protected openTicket(id: number): void {
    this.ticketDialog()?.open(id);
  }
}
