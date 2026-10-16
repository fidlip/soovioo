import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  Message,
  MessagesApiService,
  SchoolClass,
  Student,
  StudentsApiService,
} from '@sova/shared';

type SendMode = 'individual' | 'class' | 'school';

const MODE_OPTIONS: { value: SendMode; label: string }[] = [
  { value: 'individual', label: 'Jednotlivci — vybraný žák' },
  { value: 'class', label: 'Celá třída' },
  { value: 'school', label: 'Celá škola' },
];

type ReadFilter = 'all' | 'unread' | 'read';

const READ_FILTER_OPTIONS: { value: ReadFilter; label: string }[] = [
  { value: 'all', label: 'Vše' },
  { value: 'unread', label: 'Nepřečtené' },
  { value: 'read', label: 'Přečtené' },
];

@Component({
  selector: 'app-messages-page',
  imports: [FormsModule, Button, Card, InputText, PMessage, Select, Tag, Textarea],
  templateUrl: './messages-page.html',
})
export class MessagesPage {
  private readonly messagesApi = inject(MessagesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly messages = signal<Message[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly broadcastClasses = signal<SchoolClass[]>([]);
  protected readonly broadcastResult = signal<string | null>(null);
  protected readonly broadcastError = signal<string | null>(null);

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  /** "Celá škola" only makes sense to offer to Admin — Teacher's school-wide
   * broadcast is rejected server-side anyway (see MessageBroadcastController). */
  protected readonly modeOptions = computed(() =>
    this.authApi.isAdmin() ? MODE_OPTIONS : MODE_OPTIONS.filter((o) => o.value !== 'school'),
  );

  protected selectedStudentId: number | null = null;
  protected sendMode: SendMode = 'individual';
  protected selectedBroadcastClassId: number | null = null;
  protected subject = '';
  protected body = '';

  protected readonly readFilterOptions = READ_FILTER_OPTIONS;
  protected readFilter: ReadFilter = 'all';

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.selectedStudentId ??= students[0]?.id ?? null;
        this.reload();
      },
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.canSendMessages()) {
      this.messagesApi.getBroadcastableClasses().subscribe({
        next: (classes) => {
          this.broadcastClasses.set(classes);
          this.selectedBroadcastClassId ??= classes[0]?.id ?? null;
        },
        error: (err) => console.error('getBroadcastableClasses failed', err),
      });
    }
  }

  protected canSendMessages(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected onStudentChange(): void {
    this.reload();
  }

  protected send(): void {
    if (!this.subject || !this.body) {
      return;
    }
    this.broadcastResult.set(null);
    this.broadcastError.set(null);

    if (this.sendMode === 'individual') {
      if (!this.selectedStudentId) {
        return;
      }
      this.messagesApi
        .sendMessage(this.selectedStudentId, { subject: this.subject, body: this.body })
        .subscribe({
          next: () => {
            this.clearForm();
            this.reload();
          },
          error: (err) => this.handleSendError(err),
        });
      return;
    }

    if (this.sendMode === 'class') {
      if (!this.selectedBroadcastClassId) {
        return;
      }
      this.messagesApi
        .broadcastToClass(this.selectedBroadcastClassId, { subject: this.subject, body: this.body })
        .subscribe({
          next: (result) => {
            this.broadcastResult.set(`Odesláno ${result.recipientCount} žákům ve třídě.`);
            this.clearForm();
            this.reload();
          },
          error: (err) => this.handleSendError(err),
        });
      return;
    }

    this.messagesApi.broadcastToSchool({ subject: this.subject, body: this.body }).subscribe({
      next: (result) => {
        this.broadcastResult.set(`Odesláno ${result.recipientCount} žákům celé školy.`);
        this.clearForm();
        this.reload();
      },
      error: (err) => this.handleSendError(err),
    });
  }

  protected filteredMessages(): Message[] {
    if (this.readFilter === 'all') {
      return this.messages();
    }
    return this.messages().filter((m) => (this.readFilter === 'unread' ? !m.readAt : !!m.readAt));
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

  /** No edit action exists for messages on purpose — a sent message is
   * immutable, like an email (see MessagesController). */
  protected deleteMessage(message: Message): void {
    if (!this.selectedStudentId) {
      return;
    }
    this.messagesApi.deleteMessage(this.selectedStudentId, message.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteMessage failed', err),
    });
  }

  private clearForm(): void {
    this.subject = '';
    this.body = '';
  }

  private handleSendError(err: unknown): void {
    console.error('send failed', err);
    this.broadcastError.set(
      this.sendMode === 'individual'
        ? 'Odeslání se nezdařilo.'
        : 'Odeslání se nezdařilo — nemáte na tuto skupinu příjemců oprávnění.',
    );
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
