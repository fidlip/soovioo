import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import {
  DIGEST_TIMES,
  NOTIFICATION_TYPE_LABELS,
  NotificationType,
  NotificationsApiService,
  digestTimeLabel,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** The school's daily e-mail of notifications — its default time and the
 * kinds nobody can switch off. */
@Component({
  selector: 'app-school-notifications',
  imports: [
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    MultiSelect,
    Select,
    InfoHint,
  ],
  templateUrl: './school-notifications.html',
})
export class SchoolNotifications {
  private readonly notificationsApi = inject(NotificationsApiService);
  private readonly notify = inject(NotifyService);

  protected readonly timeOptions = DIGEST_TIMES;
  protected readonly typeOptions = (
    Object.keys(NOTIFICATION_TYPE_LABELS) as NotificationType[]
  ).map((value) => ({ value, label: NOTIFICATION_TYPE_LABELS[value] }));

  protected readonly digestTime = signal('17:00');
  protected readonly forcedEmail = signal<NotificationType[]>([]);

  constructor() {
    this.notificationsApi.getSchoolSettings().subscribe({
      next: (settings) => {
        this.digestTime.set(digestTimeLabel(settings.digestTime));
        this.forcedEmail.set(settings.forcedEmail);
      },
      error: (err) =>
        console.error('getSchoolNotificationSettings failed', err),
    });
  }

  protected save(): void {
    this.notificationsApi
      .setSchoolSettings({
        digestTime: this.digestTime(),
        forcedEmail: this.forcedEmail(),
      })
      .subscribe({
        next: (settings) => {
          this.digestTime.set(digestTimeLabel(settings.digestTime));
          this.forcedEmail.set(settings.forcedEmail);
          this.notify.success('Uloženo.');
        },
        error: (err) =>
          this.notify.actionFailed('setSchoolNotificationSettings failed', err),
      });
  }
}
