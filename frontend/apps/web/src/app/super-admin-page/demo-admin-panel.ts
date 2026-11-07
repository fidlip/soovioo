import { Component, inject, signal } from '@angular/core';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import { DateFormatPipe, DemoApiService, DemoContact } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { Loading } from '../loading/loading';

/** Správa škol → who left a contact in a "Vyzkoušet" demo (CSV export).
 * Their feedback has its own page (Zpětná vazba). */
@Component({
  selector: 'app-demo-admin-panel',
  imports: [
    Button,
    Card,
    DateFormatPipe,
    Loading,
    PrimeTemplate,
    TableModule,
    Tag,
  ],
  templateUrl: './demo-admin-panel.html',
})
export class DemoAdminPanel {
  private readonly demoApi = inject(DemoApiService);
  private readonly notify = inject(NotifyService);

  protected readonly contacts = signal<DemoContact[] | null>(null);

  constructor() {
    this.demoApi.contacts().subscribe({
      next: (c) => this.contacts.set(c),
      error: (err) => {
        this.notify.actionFailed('demo contacts failed', err);
        this.contacts.set([]);
      },
    });
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
