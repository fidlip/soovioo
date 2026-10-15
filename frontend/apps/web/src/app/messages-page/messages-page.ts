import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AuthApiService,
  Message,
  MessagesApiService,
  Student,
  StudentsApiService,
} from '@sova/shared';

@Component({
  selector: 'app-messages-page',
  imports: [FormsModule],
  templateUrl: './messages-page.html',
})
export class MessagesPage {
  private readonly messagesApi = inject(MessagesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly messages = signal<Message[]>([]);
  protected readonly students = signal<Student[]>([]);

  protected selectedStudentId: number | null = null;
  protected subject = '';
  protected body = '';

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.selectedStudentId ??= students[0]?.id ?? null;
        this.reload();
      },
      error: (err) => console.error('getStudents failed', err),
    });
  }

  protected canSendMessages(): boolean {
    const role = this.authApi.getRole();
    return role === 'Teacher' || role === 'Admin';
  }

  protected onStudentChange(): void {
    this.reload();
  }

  protected send(): void {
    if (!this.selectedStudentId || !this.subject || !this.body) {
      return;
    }
    this.messagesApi
      .sendMessage(this.selectedStudentId, {
        subject: this.subject,
        body: this.body,
      })
      .subscribe({
        next: () => {
          this.subject = '';
          this.body = '';
          this.reload();
        },
        error: (err) => console.error('sendMessage failed', err),
      });
  }

  protected markRead(message: Message): void {
    if (!this.selectedStudentId || message.readAt) {
      return;
    }
    this.messagesApi.markRead(this.selectedStudentId, message.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('markRead failed', err),
    });
  }

  private reload(): void {
    if (!this.selectedStudentId) {
      return;
    }
    this.messagesApi.getMessages(this.selectedStudentId).subscribe({
      next: (messages) => this.messages.set(messages),
      error: (err) => console.error('getMessages failed', err),
    });
  }
}
