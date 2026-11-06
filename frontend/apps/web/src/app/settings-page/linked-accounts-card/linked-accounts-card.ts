import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { FormsModule, NgForm } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { LinkedAccount, LinkedAccountsApiService } from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../../forms';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';

/** Nastavení → Propojené účty: the user's accounts at other schools. */
@Component({
  selector: 'app-linked-accounts-card',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    InputText,
    Message,
    Password,
  ],
  templateUrl: './linked-accounts-card.html',
})
export class LinkedAccountsCard {
  private readonly api = inject(LinkedAccountsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);
  protected readonly unread = inject(UnreadCountsService);

  protected schoolSlug = '';
  protected username = '';
  protected password = '';
  protected readonly error = signal<string | null>(null);

  protected link(form: NgForm): void {
    this.error.set(null);
    this.api
      .link({
        schoolSlug: this.schoolSlug,
        username: this.username,
        password: this.password,
      })
      .subscribe({
        next: () => {
          form.resetForm({
            linkSchoolSlug: '',
            linkUsername: '',
            linkPassword: '',
          });
          this.unread.refresh();
          this.notify.success('Účty jsou propojené.');
        },
        error: (err: HttpErrorResponse) =>
          this.error.set(
            typeof err.error === 'string'
              ? err.error
              : 'Propojení se nepovedlo.',
          ),
      });
  }

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
