import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { NativeScriptCommonModule, NativeScriptFormsModule } from '@nativescript/angular';
import {
  AuthApiService,
  Message,
  MessagesApiService,
  Student,
  StudentsApiService,
} from '@sova/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-messages',
  templateUrl: './messages.component.html',
  imports: [NativeScriptCommonModule, NativeScriptFormsModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class MessagesComponent {
  private readonly messagesApi = inject(MessagesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly messages = signal<Message[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected selectedStudentIndex = 0;

  protected subject = '';
  protected body = '';

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.reload();
      },
      error: (err) => console.error('getStudents failed', err),
    });
  }

  protected canSendMessages(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected send(): void {
    const student = this.students()[this.selectedStudentIndex];
    if (!student || !this.subject || !this.body) {
      return;
    }
    this.messagesApi.sendMessage(student.id, { subject: this.subject, body: this.body }).subscribe({
      next: () => {
        this.subject = '';
        this.body = '';
        this.reload();
      },
      error: (err) => console.error('sendMessage failed', err),
    });
  }

  protected markRead(message: Message): void {
    const student = this.students()[this.selectedStudentIndex];
    if (!student || message.readAt) {
      return;
    }
    this.messagesApi.markRead(student.id, message.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('markRead failed', err),
    });
  }

  private reload(): void {
    const student = this.students()[this.selectedStudentIndex];
    if (!student) {
      return;
    }
    this.messagesApi.getMessages(student.id).subscribe({
      next: (messages) => this.messages.set(messages),
      error: (err) => console.error('getMessages failed', err),
    });
  }
}
