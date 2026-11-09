import { SupportTicketStatus } from '@soovioo/shared';

export function ticketStatusSeverity(
  status: SupportTicketStatus,
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
