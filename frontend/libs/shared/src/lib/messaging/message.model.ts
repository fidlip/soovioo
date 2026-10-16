export interface Message {
  id: number;
  studentId: number;
  senderName: string;
  subject: string;
  body: string;
  sentAt: string;
  readAt?: string;
}

export interface SendMessageRequest {
  subject: string;
  body: string;
}

/** Fan-out send result — one Message row was created per recipient's thread. */
export interface BroadcastResult {
  recipientCount: number;
}
