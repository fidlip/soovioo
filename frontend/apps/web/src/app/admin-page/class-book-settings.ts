import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import { MySchoolApiService, UnverifiedExcuseApproval } from '@soovioo/shared';
import { FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** Třídní kniha in Údaje o škole: whether an omluvenka's day is approved
 * before all its lessons are written. */
@Component({
  selector: 'app-class-book-settings',
  imports: [Button, Card, FloatField, FormsModule, InfoHint, Select],
  templateUrl: './class-book-settings.html',
})
export class ClassBookSettings {
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly notify = inject(NotifyService);

  protected readonly options: {
    value: UnverifiedExcuseApproval;
    label: string;
  }[] = [
    { value: 'Warn', label: 'Varovat a nechat schválit' },
    { value: 'Block', label: 'Nepovolit' },
  ];
  protected readonly approval = signal<UnverifiedExcuseApproval>('Warn');

  constructor() {
    this.mySchoolApi.getClassBookSettings().subscribe({
      next: (settings) => this.approval.set(settings.unverifiedExcuseApproval),
      error: (err) => console.error('getClassBookSettings failed', err),
    });
  }

  protected save(): void {
    this.mySchoolApi
      .setClassBookSettings({ unverifiedExcuseApproval: this.approval() })
      .subscribe({
        next: (settings) => {
          this.approval.set(settings.unverifiedExcuseApproval);
          this.notify.success('Uloženo.');
        },
        error: (err) =>
          this.notify.actionFailed('setClassBookSettings failed', err),
      });
  }
}
