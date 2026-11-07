import { Component, inject } from '@angular/core';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { LinkedAccount, LinkedAccountsApiService } from '@soovioo/shared';
import { LinkAccountDialogService } from '../../link-account/link-account-dialog.service';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';

/** Nastavení → Propojené účty: the user's accounts at other schools. */
@Component({
  selector: 'app-linked-accounts-card',
  imports: [Button, Card],
  templateUrl: './linked-accounts-card.html',
})
export class LinkedAccountsCard {
  private readonly api = inject(LinkedAccountsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);
  protected readonly unread = inject(UnreadCountsService);
  protected readonly dialog = inject(LinkAccountDialogService);

  protected unlink(account: LinkedAccount): void {
    this.confirmation.confirm({
      header: 'Zrušit propojení',
      message: `Zrušit propojení s účtem ${account.username} (${account.schoolName})? Upozornění z něj přestanou chodit na telefony přihlášené jiným účtem.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Zrušit propojení',
      rejectLabel: 'Ponechat',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.api.unlink(account.userId).subscribe({
          next: () => this.unread.refresh(),
          error: (err) => this.notify.actionFailed('unlink failed', err),
        }),
    });
  }
}
