import { SupportTicketEntry } from '@soovioo/shared';
import {
  formatMinutes,
  groupTicketThread,
  hiddenWhenFolded,
  visitMinutes,
} from './ticket-thread';

let id = 0;
function entry(
  kind: SupportTicketEntry['kind'],
  at: string,
  more: Partial<SupportTicketEntry> = {},
): SupportTicketEntry {
  return {
    id: ++id,
    at,
    kind,
    authorName: 'superadmin',
    bySupport: true,
    text: null,
    status: null,
    until: null,
    sessionId: null,
    statusCode: null,
    ...more,
  };
}

describe('groupTicketThread', () => {
  it('puts what happened during a visit into it, the rest stays in the thread', () => {
    const created = entry('Created', '10:00');
    const status = entry('StatusChanged', '10:01', {
      status: 'InProgress',
      sessionId: 7,
    });
    const change = entry('SessionAction', '10:02', {
      sessionId: 7,
      text: 'Upravena známka',
    });
    const message = entry('Message', '10:03', { text: 'Hotovo?' });
    const revoked = entry('AccessRevoked', '10:04');
    const after = entry('Message', '10:06', { text: 'Díky' });

    const items = groupTicketThread([
      created,
      // Entering set it a moment before the visit started.
      status,
      entry('SessionStarted', '10:01', { sessionId: 7 }),
      change,
      message,
      revoked,
      entry('SessionEnded', '10:04', { sessionId: 7 }),
      after,
    ]);

    expect(items.map((i) => i.entry?.id ?? 'visit')).toEqual([
      created.id,
      'visit',
      after.id,
    ]);
    const visit = items[1].visit!;
    expect(visit).toMatchObject({
      sessionId: 7,
      from: '10:01',
      to: '10:04',
      end: 'ended',
      result: null,
      changes: 1,
    });
    expect(visit.entries).toEqual([status, change, message, revoked]);
    expect(visit.entries.filter((e) => !hiddenWhenFolded(e))).toEqual([
      message,
      revoked,
    ]);
  });

  it('keeps a running visit open to the end, a left one carries what was done', () => {
    const items = groupTicketThread([
      entry('SessionStarted', '09:00', { sessionId: 1 }),
      entry('Visit', '09:30', { sessionId: 1, text: 'Opraveno' }),
      entry('SessionStarted', '10:00', { sessionId: 2 }),
      entry('Message', '10:05'),
    ]);
    expect(items.map((i) => i.visit?.end)).toEqual(['left', 'running']);
    expect(items[0].visit!.result).toBe('Opraveno');
    expect(items[1].visit!.to).toBeNull();
    expect(items[1].visit!.entries).toHaveLength(1);
  });
});

describe('visit time', () => {
  it('counts a running visit until now', () => {
    const [left, running] = groupTicketThread([
      entry('SessionStarted', '2026-11-12T10:00:00Z', { sessionId: 1 }),
      entry('Visit', '2026-11-12T10:25:30Z', { sessionId: 1 }),
      entry('SessionStarted', '2026-11-12T11:00:00Z', { sessionId: 2 }),
    ]).map((i) => i.visit!);
    const now = new Date('2026-11-12T12:05:00Z').getTime();
    expect(visitMinutes(left, now)).toBe(25.5);
    expect(visitMinutes(running, now)).toBe(65);
  });

  it('reads as hours and minutes', () => {
    expect(formatMinutes(0.4)).toBe('<1 min');
    expect(formatMinutes(25.5)).toBe('26 min');
    expect(formatMinutes(65)).toBe('1 h 5 min');
    expect(formatMinutes(120)).toBe('2 h');
  });
});

describe('an inactive visit', () => {
  it('lasts until its tab closed', () => {
    const [visit] = groupTicketThread([
      entry('SessionStarted', '2026-11-12T10:00:00Z', {
        sessionId: 1,
        inactiveSince: '2026-11-12T10:20:00Z',
      }),
    ]).map((i) => i.visit!);
    expect(visit.inactiveSince).toBe('2026-11-12T10:20:00Z');
    expect(
      visitMinutes(visit, new Date('2026-11-12T11:00:00Z').getTime()),
    ).toBe(20);
  });
});
