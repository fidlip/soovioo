import { DatePipe } from '@angular/common';
import {
  Component,
  WritableSignal,
  computed,
  inject,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import { Textarea } from '@openng/optimus-ui/textarea';
import { Observable, forkJoin, of } from 'rxjs';
import {
  AuthApiService,
  Contact,
  Conversation,
  ConversationSummary,
  Message,
  MessageAttachment,
  MessagesApiService,
  SchoolClass,
  Student,
  StudentsApiService,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { UnreadMessagesService } from '../unread-messages.service';
import { NotifyService } from '../notify.service';

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

/** Staff only — parents/pupils always pick recipients from contacts. */
type SendMode = 'individual' | 'class' | 'school';

const MODE_OPTIONS: { value: SendMode; label: string }[] = [
  { value: 'individual', label: 'Rodině vybraného žáka' },
  { value: 'class', label: 'Celé třídě (každé rodině zvlášť)' },
  { value: 'school', label: 'Celé škole (každé rodině zvlášť)' },
];

type ReadFilter = 'all' | 'unread';

const READ_FILTER_OPTIONS: { value: ReadFilter; label: string }[] = [
  { value: 'all', label: 'Vše' },
  { value: 'unread', label: 'Nepřečtené' },
];

/** Conversations: list on the left, the open one on the right (on a phone
 * one at a time). Everyone in a conversation can reply. */
@Component({
  selector: 'app-messages-page',
  imports: [
    TermPipe,
    AppIcon,
    DatePipe,
    FormsModule,
    Button,
    Dialog,
    InputText,
    MultiSelect,
    PMessage,
    Select,
    Textarea,
  ],
  templateUrl: './messages-page.html',
})
export class MessagesPage {
  protected readonly notify = inject(NotifyService);
  private readonly messagesApi = inject(MessagesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);
  private readonly unread = inject(UnreadMessagesService);
  private readonly term = (text: string) =>
    localizeStudentTerm(text, this.authApi.getSchoolType());

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly selected = signal<Conversation | null>(null);
  protected readonly students = signal<Student[]>([]);
  protected readonly broadcastClasses = signal<SchoolClass[]>([]);
  protected readonly contacts = signal<Contact[]>([]);
  protected readonly error = signal<string | null>(null);
  protected readonly myUserId = this.authApi.getUserId();
  protected readonly isStaff = this.authApi.isTeacherOrAdmin();

  protected readonly readFilterOptions = READ_FILTER_OPTIONS;
  protected readonly readFilter = signal<ReadFilter>('all');
  protected readonly visibleConversations = computed(() =>
    this.readFilter() === 'unread'
      ? this.conversations().filter((c) => c.unread)
      : this.conversations(),
  );

  protected readonly modeOptions = MODE_OPTIONS.filter(
    (o) => this.authApi.isAdmin() || o.value !== 'school',
  ).map((o) => ({ ...o, label: this.term(o.label) }));

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
    })),
  );
  protected readonly contactOptions = computed(() =>
    this.contacts().map((c) => ({
      id: c.userId,
      label: c.note ? `${c.name} — ${c.note}` : c.name,
    })),
  );

  // New-conversation dialog.
  protected readonly dialogVisible = signal(false);
  protected sendMode: SendMode = 'individual';
  protected studentId: number | null = null;
  protected broadcastClassId: number | null = null;
  protected recipientIds: number[] = [];
  protected subject = '';
  protected body = '';
  protected newFile: File | null = null;
  protected readonly fileError = signal<string | null>(null);

  // Reply box under the open conversation.
  protected replyBody = '';
  protected replyFile: File | null = null;
  protected readonly replyFileError = signal<string | null>(null);

  constructor() {
    this.reloadList();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.isStaff) {
      this.messagesApi.getBroadcastableClasses().subscribe({
        next: (classes) => this.broadcastClasses.set(classes),
        error: (err) => console.error('getBroadcastableClasses failed', err),
      });
    }
  }

  protected open(summary: ConversationSummary): void {
    this.messagesApi.getConversation(summary.id).subscribe({
      next: (conversation) => {
        this.selected.set(conversation);
        this.replyBody = '';
        this.replyFile = null;
        if (summary.unread) {
          this.messagesApi.markRead(summary.id).subscribe({
            next: () => {
              this.conversations.update((list) =>
                list.map((c) =>
                  c.id === summary.id ? { ...c, unread: false } : c,
                ),
              );
              this.unread.refresh();
            },
            error: (err) => console.error('markRead failed', err),
          });
        }
      },
      error: (err) => console.error('getConversation failed', err),
    });
  }

  protected closeConversation(): void {
    this.selected.set(null);
  }

  protected openNewDialog(): void {
    this.error.set(null);
    this.fileError.set(null);
    this.sendMode = 'individual';
    this.subject = '';
    this.body = '';
    this.newFile = null;
    this.recipientIds = [];
    this.studentId = this.students()[0]?.id ?? null;
    this.broadcastClassId = this.broadcastClasses()[0]?.id ?? null;
    this.loadContacts();
    this.notify.clear();
    this.dialogVisible.set(true);
  }

  /** Parent/pupil: who they may write to depends on the child's class. */
  protected loadContacts(): void {
    if (this.isStaff || this.studentId === null) {
      return;
    }
    this.messagesApi.getContacts(this.studentId).subscribe({
      next: (contacts) => {
        this.contacts.set(contacts);
        const allowed = new Set(contacts.map((c) => c.userId));
        this.recipientIds = this.recipientIds.filter((id) => allowed.has(id));
      },
      error: (err) => console.error('getContacts failed', err),
    });
  }

  protected onNewFileSelected(event: Event): void {
    this.newFile = this.pickFile(event, this.fileError);
  }

  protected onReplyFileSelected(event: Event): void {
    this.replyFile = this.pickFile(event, this.replyFileError);
  }

  protected send(): void {
    if (!this.subject.trim() || !this.body.trim()) {
      return;
    }
    this.error.set(null);
    const request = { subject: this.subject, body: this.body };

    if (this.isStaff && this.sendMode !== 'individual') {
      const broadcast =
        this.sendMode === 'class'
          ? this.broadcastClassId === null
            ? null
            : this.messagesApi.broadcastToClass(this.broadcastClassId, request)
          : this.messagesApi.broadcastToSchool(request);
      broadcast?.subscribe({
        next: (result) => {
          const skipped = result.skippedCount
            ? this.term(
                ` (${result.skippedCount} žáků bez účtu i rodičů přeskočeno)`,
              )
            : '';
          this.uploadThen(
            result.messages,
            this.newFile,
            `Odesláno ${result.recipientCount} rodinám${skipped}.`,
          );
        },
        error: (err) => this.handleSendError(err),
      });
      return;
    }

    if (this.studentId === null) {
      return;
    }
    if (!this.isStaff && this.recipientIds.length === 0) {
      this.error.set('Vyberte aspoň jednoho příjemce.');
      return;
    }
    this.messagesApi
      .startConversation({
        ...request,
        studentId: this.studentId,
        recipientUserIds: this.isStaff ? undefined : this.recipientIds,
      })
      .subscribe({
        next: (conversation) =>
          this.uploadThen(
            [
              {
                conversationId: conversation.id,
                messageId: conversation.messages[0].id,
              },
            ],
            this.newFile,
            null,
            conversation.id,
          ),
        error: (err) => this.handleSendError(err),
      });
  }

  protected sendReply(): void {
    const conversation = this.selected();
    if (!conversation || !this.replyBody.trim()) {
      return;
    }
    this.messagesApi.reply(conversation.id, this.replyBody).subscribe({
      next: (message) => {
        const file = this.replyFile;
        this.replyBody = '';
        this.replyFile = null;
        this.uploadAll(
          [{ conversationId: conversation.id, messageId: message.id }],
          file,
        ).subscribe(() => this.reloadSelectedAndList());
      },
      error: (err) => this.notify.actionFailed('reply failed', err),
    });
  }

  /** Sender only — the backend refuses anyone else. */
  protected deleteMessage(message: Message): void {
    const conversation = this.selected();
    if (!conversation) {
      return;
    }
    this.messagesApi.deleteMessage(conversation.id, message.id).subscribe({
      next: () => {
        if (conversation.messages.length === 1) {
          this.selected.set(null);
          this.reloadList();
        } else {
          this.reloadSelectedAndList();
        }
      },
      error: (err) => this.notify.actionFailed('deleteMessage failed', err),
    });
  }

  protected isMine(message: Message): boolean {
    return message.senderUserId === this.myUserId;
  }

  protected otherParticipants(conversation: Conversation): string {
    return conversation.participants
      .filter((p) => p.userId !== this.myUserId)
      .map((p) => p.name)
      .join(', ');
  }

  /** Same blob-download pattern as grades-page.ts's downloadReportCard —
   * goes through HttpClient (so the auth interceptor attaches the bearer
   * token) rather than a plain <a href>. */
  protected downloadAttachment(
    message: Message,
    attachment: MessageAttachment,
  ): void {
    const conversation = this.selected();
    if (!conversation) {
      return;
    }
    this.messagesApi
      .downloadAttachment(conversation.id, message.id, attachment.id)
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

  private pickFile(
    event: Event,
    error: WritableSignal<string | null>,
  ): File | null {
    const file = (event.target as HTMLInputElement).files?.[0] ?? null;
    error.set(null);
    if (!file) {
      return null;
    }
    if (file.size > MAX_ATTACHMENT_SIZE_BYTES) {
      error.set('Soubor je příliš velký (max 10 MB).');
      return null;
    }
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
    if (!ALLOWED_ATTACHMENT_EXTENSIONS.includes(extension)) {
      error.set('Nepovolený typ souboru.');
      return null;
    }
    return file;
  }

  /** After a successful send: attach the file to every created message,
   * close the dialog, show the outcome and (single conversation) open it. */
  private uploadThen(
    refs: { conversationId: number; messageId: number }[],
    file: File | null,
    successNotice: string | null,
    openId?: number,
  ): void {
    this.uploadAll(refs, file).subscribe((uploaded) => {
      this.dialogVisible.set(false);
      if (!uploaded) {
        this.notify.warn(
          'Zpráva byla odeslána, ale příloha se nepodařila nahrát.',
        );
      } else if (successNotice) {
        this.notify.success(successNotice);
      }
      this.reloadList(openId);
    });
  }

  /** Emits once: true = all uploads (or none needed) went through. */
  private uploadAll(
    refs: { conversationId: number; messageId: number }[],
    file: File | null,
  ): Observable<boolean> {
    if (!file || refs.length === 0) {
      return of(true);
    }
    return new Observable<boolean>((subscriber) => {
      forkJoin(
        refs.map((r) =>
          this.messagesApi.uploadAttachment(
            r.conversationId,
            r.messageId,
            file,
          ),
        ),
      ).subscribe({
        next: () => {
          subscriber.next(true);
          subscriber.complete();
        },
        error: (err) => {
          console.error('uploadAttachment failed', err);
          subscriber.next(false);
          subscriber.complete();
        },
      });
    });
  }

  private handleSendError(err: { error?: unknown }): void {
    console.error('send failed', err);
    this.error.set(
      typeof err.error === 'string' ? err.error : 'Odeslání se nezdařilo.',
    );
  }

  private reloadList(openId?: number): void {
    this.messagesApi.getConversations().subscribe({
      next: (conversations) => {
        this.conversations.set(conversations);
        const toOpen = conversations.find((c) => c.id === openId);
        if (toOpen) {
          this.open(toOpen);
        }
      },
      error: (err) => console.error('getConversations failed', err),
    });
    this.unread.refresh();
  }

  private reloadSelectedAndList(): void {
    const conversation = this.selected();
    if (conversation) {
      this.messagesApi.getConversation(conversation.id).subscribe({
        next: (c) => this.selected.set(c),
        error: (err) => console.error('getConversation failed', err),
      });
    }
    this.reloadList();
  }
}
