import { DatePipe } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Card } from '@openng/optimus-ui/card';
import { Tag } from '@openng/optimus-ui/tag';
import { SupportApiService, SupportSession } from '@soovioo/shared';
import { AppIcon } from '../app-icon';

/** SuperAdmin agenda "Logy" — audit trails; for now the support-mode visits
 * to schools and what was changed during them. */
@Component({
  selector: 'app-logs-page',
  imports: [AppIcon, Card, DatePipe, Tag],
  templateUrl: './logs-page.html',
})
export class LogsPage {
  private readonly supportApi = inject(SupportApiService);

  protected readonly supportSessions = signal<SupportSession[]>([]);

  constructor() {
    this.supportApi.getSupportSessions().subscribe({
      next: (sessions) => this.supportSessions.set(sessions),
      error: (err) => console.error('getSupportSessions failed', err),
    });
  }

  protected changesLabel(count: number): string {
    return count === 1
      ? '1 změna'
      : count >= 2 && count <= 4
        ? `${count} změny`
        : `${count} změn`;
  }
}
