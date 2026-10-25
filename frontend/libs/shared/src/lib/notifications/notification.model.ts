export type NotificationType =
  'Grade' | 'Message' | 'Absence' | 'Excuse' | 'Homework' | 'Event' | 'Photo';

export interface AppNotification {
  id: number;
  type: NotificationType;
  title: string;
  body: string | null;
  /** The school's page it opens, e.g. `grades`. */
  page: string;
  /** What on that page — the conversation for `messages`. */
  targetId: number | null;
  createdAt: string;
  read: boolean;
}

export interface UnreadCounts {
  notifications: number;
  /** Conversations with something unread. */
  messages: number;
}

export interface NotificationPage {
  items: AppNotification[];
  hasMore: boolean;
}

export interface NotificationTypeSetting {
  type: NotificationType;
  /** Under the bell. */
  enabled: boolean;
  /** In the daily e-mail. */
  email: boolean;
  /** The school sends it by e-mail to everyone. */
  emailForced: boolean;
}

export interface NotificationSettings {
  types: NotificationTypeSetting[];
  /** The account has an e-mail address for the daily e-mail. */
  hasEmail: boolean;
  /** `HH:mm:ss`, null = `schoolDigestTime`. */
  digestTime: string | null;
  schoolDigestTime: string;
}

/** Admin: the school's daily e-mail of notifications. */
export interface SchoolNotificationSettings {
  /** `HH:mm:ss`, for everyone who didn't pick their own. */
  digestTime: string;
  /** Kinds nobody can switch off in the e-mail. */
  forcedEmail: NotificationType[];
}

export interface UnsubscribeResult {
  /** Kinds that keep coming — the school sends them to everyone. */
  forced: NotificationType[];
}

export const NOTIFICATION_TYPE_LABELS: Record<NotificationType, string> = {
  Grade: 'Nové známky',
  Message: 'Nové zprávy',
  Absence: 'Zapsané absence',
  Excuse: 'Vyřízené omluvenky',
  Homework: 'Nové domácí úkoly',
  Event: 'Nové a změněné akce v kalendáři',
  Photo: 'Fotky dětí ke schválení',
};

/** Times offered for the daily e-mail, `HH:mm` every half hour. */
export const DIGEST_TIMES: string[] = Array.from(
  { length: 48 },
  (_, i) =>
    `${String(Math.floor(i / 2)).padStart(2, '0')}:${i % 2 ? '30' : '00'}`,
);

/** `17:00:00` → `17:00`. */
export function digestTimeLabel(time: string): string {
  return time.slice(0, 5);
}
