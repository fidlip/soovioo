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
  enabled: boolean;
}

export interface NotificationSettings {
  types: NotificationTypeSetting[];
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
