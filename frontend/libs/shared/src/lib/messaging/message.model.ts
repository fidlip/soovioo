export interface MessageAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface Message {
  id: number;
  /** Null only for messages migrated from accounts that no longer exist. */
  senderUserId: number | null;
  senderName: string;
  body: string;
  sentAt: string;
  attachments: MessageAttachment[];
}

export interface ConversationParticipant {
  userId: number;
  name: string;
}

/** One row of the conversation list. */
export interface ConversationSummary {
  id: number;
  subject: string;
  /** The pupil it's about, if any. */
  studentId: number | null;
  studentName: string | null;
  participants: string[];
  lastMessageAt: string;
  lastSenderName: string | null;
  lastMessagePreview: string | null;
  /** Something from someone else the current user hasn't read yet. */
  unread: boolean;
}

export interface Conversation {
  id: number;
  subject: string;
  studentId: number | null;
  studentName: string | null;
  participants: ConversationParticipant[];
  /** Oldest first. */
  messages: Message[];
}

/** Staff: `studentId` = whose family to write to. Parent/pupil:
 * `studentId` = which (own) child it's about + `recipientUserIds` from
 * `getContacts`. */
export interface StartConversationRequest {
  subject: string;
  body: string;
  studentId?: number;
  recipientUserIds?: number[];
}

/** Someone a parent/pupil may write to — `note` says why (class teacher,
 * subjects taught, school leadership). */
export interface Contact {
  userId: number;
  name: string;
  note: string;
}

export interface BroadcastRequest {
  subject: string;
  body: string;
}

/** Which conversation/first message a fan-out send created — lets the
 * caller attach a file to every copy afterwards. */
export interface BroadcastMessageRef {
  conversationId: number;
  messageId: number;
}

/** One private conversation per pupil's family; `skippedCount` pupils had
 * nobody (no own account, no parent) to deliver to. */
export interface BroadcastResult {
  recipientCount: number;
  skippedCount: number;
  messages: BroadcastMessageRef[];
}
