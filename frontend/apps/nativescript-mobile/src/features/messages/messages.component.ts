import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import { NativeScriptCommonModule, NativeScriptFormsModule } from '@nativescript/angular';
import { Conversation, ConversationSummary, MessagesApiService } from '@soovioo/shared';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

/** Conversations: list → detail with a reply box. Starting a new
 * conversation is web-only for now. */
@Component({
  selector: 'ns-messages',
  templateUrl: './messages.component.html',
  imports: [NativeScriptCommonModule, NativeScriptFormsModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
})
export class MessagesComponent {
  private readonly messagesApi = inject(MessagesApiService);

  protected readonly conversations = signal<ConversationSummary[]>([]);
  protected readonly selected = signal<Conversation | null>(null);
  protected replyBody = '';

  constructor() {
    this.reload();
  }

  protected open(index: number): void {
    const summary = this.conversations()[index];
    if (!summary) {
      return;
    }
    this.messagesApi.getConversation(summary.id).subscribe({
      next: (conversation) => {
        this.selected.set(conversation);
        if (summary.unread) {
          this.messagesApi.markRead(summary.id).subscribe({
            next: () => this.reload(),
            error: (err) => console.error('markRead failed', err),
          });
        }
      },
      error: (err) => console.error('getConversation failed', err),
    });
  }

  protected close(): void {
    this.selected.set(null);
  }

  protected reply(): void {
    const conversation = this.selected();
    if (!conversation || !this.replyBody.trim()) {
      return;
    }
    this.messagesApi.reply(conversation.id, this.replyBody).subscribe({
      next: () => {
        this.replyBody = '';
        this.messagesApi.getConversation(conversation.id).subscribe({
          next: (c) => this.selected.set(c),
          error: (err) => console.error('getConversation failed', err),
        });
        this.reload();
      },
      error: (err) => console.error('reply failed', err),
    });
  }

  private reload(): void {
    this.messagesApi.getConversations().subscribe({
      next: (conversations) => this.conversations.set(conversations),
      error: (err) => console.error('getConversations failed', err),
    });
  }
}
