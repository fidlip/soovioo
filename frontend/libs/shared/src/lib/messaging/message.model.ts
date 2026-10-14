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
  senderName: string;
  subject: string;
  body: string;
}
