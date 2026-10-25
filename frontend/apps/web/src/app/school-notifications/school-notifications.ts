import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Select } from '@openng/optimus-ui/select';
import { ToggleSwitch } from '@openng/optimus-ui/toggleswitch';
import {
  DIGEST_TIMES,
  NOTIFICATION_TYPE_LABELS,
  NotificationType,
  NotificationsApiService,
  SchoolNotificationSettings,
  digestTimeLabel,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** The school's daily e-mail of notifications — its default time and the
 * kinds nobody can switch off; push — whether it may carry the whole text,
 * and the night quiet. */
@Component({
  selector: 'app-school-notifications',
  imports: [
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    MultiSelect,
    Select,
    ToggleSwitch,
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
  protected readonly pushFullText = signal(false);
  protected readonly pushQuietFrom = signal('21:00');
  protected readonly pushQuietTo = signal('07:00');

  constructor() {
    this.notificationsApi.getSchoolSettings().subscribe({
      next: (settings) => this.apply(settings),
      error: (err) =>
        console.error('getSchoolNotificationSettings failed', err),
    });
  }

  private apply(settings: SchoolNotificationSettings): void {
    this.digestTime.set(digestTimeLabel(settings.digestTime));
    this.forcedEmail.set(settings.forcedEmail);
    this.pushFullText.set(settings.pushFullText);
    this.pushQuietFrom.set(digestTimeLabel(settings.pushQuietFrom));
    this.pushQuietTo.set(digestTimeLabel(settings.pushQuietTo));
  }

  protected save(): void {
    this.notificationsApi
      .setSchoolSettings({
        digestTime: this.digestTime(),
        forcedEmail: this.forcedEmail(),
        pushFullText: this.pushFullText(),
        pushQuietFrom: this.pushQuietFrom(),
        pushQuietTo: this.pushQuietTo(),
      })
      .subscribe({
        next: (settings) => {
          this.apply(settings);
          this.notify.success('Uloženo.');
        },
        error: (err) =>
          this.notify.actionFailed('setSchoolNotificationSettings failed', err),
      });
  }
}
