export interface MessageAttachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
}

export interface Message {
  id: number;
  studentId: number;
  senderName: string;
  subject: string;
  body: string;
  sentAt: string;
  readAt?: string;
  attachments: MessageAttachment[];
}

export interface SendMessageRequest {
  subject: string;
  body: string;
}

/** Which message in which student's thread a fan-out send created — lets
 * the caller attach a file to every copy afterwards (one upload call per
 * message, see MessagesApiService.uploadAttachment). */
export interface BroadcastMessageRef {
  messageId: number;
  studentId: number;
}

/** Fan-out send result — one Message row was created per recipient's thread. */
export interface BroadcastResult {
  recipientCount: number;
  messages: BroadcastMessageRef[];
}
