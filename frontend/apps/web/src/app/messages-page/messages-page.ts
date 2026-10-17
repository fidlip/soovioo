import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import { forkJoin } from 'rxjs';
import {
  AuthApiService,
  Message,
  MessageAttachment,
  MessagesApiService,
  SchoolClass,
  Student,
  StudentsApiService,
} from '@sova/shared';

/** Mirrors MessageAttachmentRules on the backend — client-side only gives
 * faster feedback, the server still enforces this for real. */
const MAX_ATTACHMENT_SIZE_BYTES = 10 * 1024 * 1024;
const ALLOWED_ATTACHMENT_EXTENSIONS = [
  '.pdf',
  '.jpg',
  '.jpeg',
  '.png',
  '.gif',
  '.webp',
  '.txt',
  '.doc',
  '.docx',
  '.xls',
  '.xlsx',
  '.ppt',
  '.pptx',
  '.odt',
  '.ods',
  '.odp',
];

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
  imports: [
    FormsModule,
    Button,
    Card,
    Dialog,
    InputText,
    PMessage,
    Select,
    Tag,
    Textarea,
  ],
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
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  /** "Celá škola" only makes sense to offer to Admin — Teacher's school-wide
   * broadcast is rejected server-side anyway (see MessageBroadcastController). */
  protected readonly modeOptions = computed(() =>
    this.authApi.isAdmin()
      ? MODE_OPTIONS
      : MODE_OPTIONS.filter((o) => o.value !== 'school'),
  );

  protected selectedStudentId: number | null = null;
  protected readonly readFilterOptions = READ_FILTER_OPTIONS;
  protected readFilter: ReadFilter = 'all';

  protected readonly messageDialogVisible = signal(false);
  protected sendMode: SendMode = 'individual';
  protected selectedBroadcastClassId: number | null = null;
  protected subject = '';
  protected body = '';
  protected selectedFile: File | null = null;
  /** Client-side file-picker validation, shown inside the still-open dialog. */
  protected readonly fileError = signal<string | null>(null);
  /** Upload failure *after* the message itself already sent — the dialog
   * has closed by then, so (like broadcastResult) this lives outside it. */
  protected readonly attachmentUploadError = signal<string | null>(null);

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

  protected openSendMessageDialog(): void {
    this.broadcastResult.set(null);
    this.broadcastError.set(null);
    this.attachmentUploadError.set(null);
    this.fileError.set(null);
    this.sendMode = 'individual';
    this.subject = '';
    this.body = '';
    this.selectedFile = null;
    this.messageDialogVisible.set(true);
  }

  protected onFileSelected(event: Event): void {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    this.fileError.set(null);
    if (!file) {
      this.selectedFile = null;
      return;
    }
    if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
      this.fileError.set('Soubor je příliš velký (max 10 MB).');
      this.selectedFile = null;
      return;
    }
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_ATTACHMENT_EXTENSIONS.includes(extension)) {
      this.fileError.set('Nepovolený typ souboru.');
      this.selectedFile = null;
      return;
    }
    this.selectedFile = file;
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
      const studentId = this.selectedStudentId;
      this.messagesApi
        .sendMessage(studentId, { subject: this.subject, body: this.body })
        .subscribe({
          next: (message) =>
            this.afterSend([{ studentId, messageId: message.id }]),
          error: (err) => this.handleSendError(err),
        });
      return;
    }

    if (this.sendMode === 'class') {
      if (!this.selectedBroadcastClassId) {
        return;
      }
      this.messagesApi
        .broadcastToClass(this.selectedBroadcastClassId, {
          subject: this.subject,
          body: this.body,
        })
        .subscribe({
          next: (result) => {
            this.broadcastResult.set(
              `Odesláno ${result.recipientCount} žákům ve třídě.`,
            );
            this.afterSend(result.messages);
          },
          error: (err) => this.handleSendError(err),
        });
      return;
    }

    this.messagesApi
      .broadcastToSchool({ subject: this.subject, body: this.body })
      .subscribe({
        next: (result) => {
          this.broadcastResult.set(
            `Odesláno ${result.recipientCount} žákům celé školy.`,
          );
          this.afterSend(result.messages);
        },
        error: (err) => this.handleSendError(err),
      });
  }

  /** Runs after every successful send/broadcast — uploads the picked file
   * (if any) to each just-created message, then closes the dialog and
   * reloads. One upload call per recipient (see MessagesApiService.
   * uploadAttachment's doc comment for why that's fine here). */
  private afterSend(
    recipients: { studentId: number; messageId: number }[],
  ): void {
    const file = this.selectedFile;
    if (!file) {
      this.clearForm();
      this.reload();
      return;
    }
    forkJoin(
      recipients.map((r) =>
        this.messagesApi.uploadAttachment(r.studentId, r.messageId, file),
      ),
    ).subscribe({
      next: () => {
        this.clearForm();
        this.reload();
      },
      error: (err) => {
        console.error('uploadAttachment failed', err);
        // The message itself was already sent — the dialog closes like any
        // other success, but this warning (unlike broadcastError) has to
        // live outside it for the same reason broadcastResult does.
        this.attachmentUploadError.set(
          'Zpráva byla odeslána, ale příloha se nepodařila nahrát.',
        );
        this.clearForm();
        this.reload();
      },
    });
  }

  protected filteredMessages(): Message[] {
    if (this.readFilter === 'all') {
      return this.messages();
    }
    return this.messages().filter((m) =>
      this.readFilter === 'unread' ? !m.readAt : !!m.readAt,
    );
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
    this.messagesApi
      .deleteMessage(this.selectedStudentId, message.id)
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('deleteMessage failed', err),
      });
  }

  private clearForm(): void {
    this.subject = '';
    this.body = '';
    this.selectedFile = null;
    this.messageDialogVisible.set(false);
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

  /** Same blob-download pattern as grades-page.ts's downloadReportCard —
   * goes through HttpClient (so the auth interceptor attaches the bearer
   * token) rather than a plain <a href>. */
  protected downloadAttachment(
    message: Message,
    attachment: MessageAttachment,
  ): void {
    this.messagesApi
      .downloadAttachment(message.studentId, message.id, attachment.id)
      .subscribe({
        next: (blob) => {
          const url = URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = attachment.fileName;
          link.click();
          URL.revokeObjectURL(url);
        },
        error: (err) => console.error('downloadAttachment failed', err),
      });
  }

  protected formatFileSize(bytes: number): string {
    if (bytes < 1024) {
      return `${bytes} B`;
    }
    if (bytes < 1024 * 1024) {
      return `${Math.round(bytes / 1024)} KB`;
    }
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  }
}
