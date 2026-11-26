import { Component, computed, input } from '@angular/core';
import { Tag } from '@openng/optimus-ui/tag';

/** A support ticket's status — the same in the app (the school's side) and
 * in the backoffice (the operator's). */
export type TicketStatus =
  | 'New'
  /** An operator took it. */
  | 'Assigned'
  /** Being worked on right now. */
  | 'InProgress'
  /** The support asked; the school's answer brings it back to Assigned. */
  | 'WaitingForSchool'
  /** Done by the support — the school closes or returns it; closes by
   * itself after 30 days. */
  | 'Resolved'
  | 'Cancelled'
  /** Confirmed by the school, or closed by itself. */
  | 'Closed';

export const TICKET_STATUS_LABELS: Record<TicketStatus, string> = {
  New: 'Nový',
  Assigned: 'Přiřazeno',
  InProgress: 'Řeší se',
  WaitingForSchool: 'Čeká na školu',
  Resolved: 'Vyřešeno',
  Cancelled: 'Zrušeno',
  Closed: 'Uzavřeno',
};

export function ticketStatusSeverity(
  status: TicketStatus,
): 'info' | 'warn' | 'success' | 'secondary' | 'contrast' {
  switch (status) {
    case 'New':
      return 'info';
    case 'InProgress':
      return 'warn';
    case 'WaitingForSchool':
      return 'contrast';
    case 'Resolved':
      return 'success';
    default:
      return 'secondary';
  }
}

/** A ticket's status as a tag that doesn't wrap. */
@Component({
  selector: 'app-ticket-status-tag',
  imports: [Tag],
  template: `<p-tag
    class="whitespace-nowrap"
    [value]="label()"
    [severity]="severity()"
  />`,
})
export class TicketStatusTag {
  readonly status = input.required<TicketStatus>();
  protected readonly label = computed(
    () => TICKET_STATUS_LABELS[this.status()],
  );
  protected readonly severity = computed(() =>
    ticketStatusSeverity(this.status()),
  );
}
