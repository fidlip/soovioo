import { Component, inject, signal } from '@angular/core';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  DemoApiService,
  DemoContact,
  DemoFeedback,
  ROLE_LABELS,
} from '@soovioo/shared';
import { NotifyService } from '../notify.service';

/** Správa škol → what the "Vyzkoušet" demos brought: contacts (CSV) and
 * feedback. */
@Component({
  selector: 'app-demo-admin-panel',
  imports: [DateFormatPipe, Button, Card, PrimeTemplate, TableModule, Tag],
  templateUrl: './demo-admin-panel.html',
})
export class DemoAdminPanel {
  private readonly demoApi = inject(DemoApiService);
  private readonly notify = inject(NotifyService);

  protected readonly contacts = signal<DemoContact[]>([]);
  protected readonly feedback = signal<DemoFeedback[]>([]);
  protected readonly roleLabels = ROLE_LABELS;

  constructor() {
    this.demoApi.contacts().subscribe({
      next: (c) => this.contacts.set(c),
      error: (err) => this.notify.actionFailed('demo contacts failed', err),
    });
    this.demoApi.allFeedback().subscribe({
      next: (f) => this.feedback.set(f),
      error: (err) => this.notify.actionFailed('demo feedback failed', err),
    });
  }

  protected stars(rating: number | null): string {
    return rating ? '★'.repeat(rating) + '☆'.repeat(5 - rating) : '—';
  }

  protected downloadCsv(): void {
    this.demoApi.contactsCsv().subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = 'soovioo-demo-kontakty.csv';
        a.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => this.notify.actionFailed('demo contacts csv failed', err),
    });
  }
}
