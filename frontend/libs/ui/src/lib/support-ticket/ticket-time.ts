/** What the support's time on a ticket is: our fault, the plan's hours, or a
 * paid service. */
export type TicketTimeCategory = 'Bug' | 'Support' | 'Service';

export const TICKET_TIME_CATEGORY_LABELS: Record<TicketTimeCategory, string> = {
  Bug: 'Chyba aplikace (nepočítá se)',
  Support: 'Podpora (z hodin plánu)',
  Service: 'Placená služba',
};

/** 75 → "1 h 15 min". */
export function formatTicketMinutes(minutes: number): string {
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours === 0
    ? `${rest} min`
    : rest === 0
      ? `${hours} h`
      : `${hours} h ${rest} min`;
}
