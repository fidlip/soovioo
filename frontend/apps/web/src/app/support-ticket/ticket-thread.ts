import { SupportTicketEntry } from '@soovioo/shared';

/** A support session in a ticket's thread: everything from its start to its
 * end, in time order. */
export interface TicketVisit {
  sessionId: number;
  authorName: string;
  from: string;
  /** Null while it runs. */
  to: string | null;
  /** Its last request. */
  lastSeen: string | null;
  /** How it ended: left ("Opustit"), ended by the school or the ticket
   * closing, run out — or still running. */
  end: 'left' | 'ended' | 'expired' | 'running';
  /** What was done, written on leaving. */
  result: string | null;
  changes: number;
  /** What happened meanwhile — its own changes and anything else. */
  entries: SupportTicketEntry[];
}

export type TicketThreadItem =
  | { visit: null; entry: SupportTicketEntry }
  | { visit: TicketVisit; entry: null };

const END_KINDS = {
  Visit: 'left',
  SessionEnded: 'ended',
  SessionExpired: 'expired',
} as const;

/** The thread (oldest first) with each visit as one item holding what
 * happened from its start to its end, and what belongs to it whenever it
 * happened (the state entering set, a moment before it started). */
export function groupTicketThread(
  entries: SupportTicketEntry[],
): TicketThreadItem[] {
  const visits = new Map<number, TicketVisit>();
  for (const entry of entries) {
    if (entry.kind === 'SessionStarted' && entry.sessionId !== null) {
      visits.set(entry.sessionId, {
        sessionId: entry.sessionId,
        authorName: entry.authorName,
        from: entry.at,
        to: null,
        lastSeen: entry.lastSeenAt ?? null,
        end: 'running',
        result: null,
        changes: 0,
        entries: [],
      });
    }
  }
  const items: TicketThreadItem[] = [];
  let open: TicketVisit | null = null;
  for (const entry of entries) {
    const own =
      entry.sessionId !== null ? visits.get(entry.sessionId) : undefined;
    if (entry.kind === 'SessionStarted' && own) {
      open = own;
      items.push({ visit: own, entry: null });
    } else if (own && entry.kind in END_KINDS) {
      own.to = entry.at;
      own.end = END_KINDS[entry.kind as keyof typeof END_KINDS];
      own.result = entry.kind === 'Visit' ? entry.text : null;
      if (open === own) {
        open = null;
      }
    } else if (own ?? open) {
      const visit = (own ?? open)!;
      if (entry.kind === 'SessionAction') {
        visit.changes++;
      }
      visit.entries.push(entry);
    } else {
      items.push({ visit: null, entry });
    }
  }
  for (const visit of visits.values()) {
    visit.entries.sort((a, b) => a.at.localeCompare(b.at));
  }
  return items;
}

/** Folded, a visit shows only the conversation and the decisions — not its
 * changes in the school and the state entering set. */
export function hiddenWhenFolded(entry: SupportTicketEntry): boolean {
  return (
    entry.kind === 'SessionAction' ||
    (entry.kind === 'StatusChanged' && entry.sessionId !== null)
  );
}

/** A running visit without a request for this long is likely a tab closed
 * without "Opustit". */
const IDLE_MINUTES = 15;

/** When a running visit went quiet — null while it's active (or over). */
export function idleSince(visit: TicketVisit, now: number): string | null {
  return visit.end === 'running' &&
    visit.lastSeen &&
    now - new Date(visit.lastSeen).getTime() > IDLE_MINUTES * 60_000
    ? visit.lastSeen
    : null;
}

/** How long a visit lasted — a running one until `now`, an idle one until
 * its last request. */
export function visitMinutes(visit: TicketVisit, now: number): number {
  const end = visit.to ?? idleSince(visit, now);
  const to = end ? new Date(end).getTime() : now;
  return Math.max(0, (to - new Date(visit.from).getTime()) / 60_000);
}

/** "<1 min", "25 min", "1 h 5 min", "2 h". */
export function formatMinutes(minutes: number): string {
  const whole = Math.round(minutes);
  if (whole < 1) {
    return '<1 min';
  }
  const h = Math.floor(whole / 60);
  const m = whole % 60;
  return h === 0 ? `${m} min` : m === 0 ? `${h} h` : `${h} h ${m} min`;
}
