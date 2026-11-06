import { HttpErrorResponse } from '@angular/common/http';
import { Component, NO_ERRORS_SCHEMA, inject, signal } from '@angular/core';
import {
  NativeScriptCommonModule,
  NativeScriptFormsModule,
} from '@nativescript/angular';
import { Dialogs } from '@nativescript/core';
import {
  LinkedAccount,
  LinkedAccountsApiService,
  NotificationsApiService,
} from '@soovioo/shared';

/** Nastavení → Propojené účty: the user's accounts at other schools. */
@Component({
  selector: 'ns-linked-accounts-section',
  templateUrl: './linked-accounts-section.component.html',
  imports: [NativeScriptCommonModule, NativeScriptFormsModule],
  schemas: [NO_ERRORS_SCHEMA],
})
export class LinkedAccountsSectionComponent {
  private readonly api = inject(LinkedAccountsApiService);
  private readonly notificationsApi = inject(NotificationsApiService);

  protected readonly accounts = signal<LinkedAccount[]>([]);
  protected schoolSlug = '';
  protected username = '';
  protected password = '';
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.load();
  }

  private load(): void {
    this.notificationsApi.getUnreadCounts().subscribe({
      next: (counts) => this.accounts.set(counts.linkedAccounts ?? []),
      error: (err) => console.error('getUnreadCounts failed', err),
    });
  }

  protected link(): void {
    this.error.set(null);
    this.api
      .link({
        schoolSlug: this.schoolSlug,
        username: this.username,
        password: this.password,
      })
      .subscribe({
        next: () => {
          this.schoolSlug = '';
          this.username = '';
          this.password = '';
          this.load();
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(
            typeof err.error === 'string'
              ? err.error
              : 'Propojení se nepovedlo.',
          ),
      });
  }

  protected async unlink(account: LinkedAccount): Promise<void> {
    const confirmed = await Dialogs.confirm({
      title: 'Zrušit propojení',
      message: `Zrušit propojení s účtem ${account.username} (${account.schoolName})?`,
      okButtonText: 'Zrušit propojení',
      cancelButtonText: 'Ponechat',
    });
    if (confirmed) {
      this.api.unlink(account.userId).subscribe({
        next: () => this.load(),
        error: (err) => console.error('unlink failed', err),
      });
    }
  }
}
