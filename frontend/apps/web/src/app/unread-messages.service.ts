import { Injectable, inject, signal } from '@angular/core';
import { MessagesApiService } from '@soovioo/shared';

/** Unread conversation count for the sidebar badge — refreshed on every
 * navigation (app.ts) and right after the Zprávy page reads/replies. */
@Injectable({ providedIn: 'root' })
export class UnreadMessagesService {
  private readonly messagesApi = inject(MessagesApiService);

  readonly count = signal(0);

  refresh(): void {
    this.messagesApi.getUnreadCount().subscribe({
      next: (count) => this.count.set(count),
      // A failed badge isn't worth a console error on every navigation.
      error: () => this.count.set(0),
    });
  }
}
