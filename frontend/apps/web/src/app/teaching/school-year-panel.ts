import { Component, computed, inject } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  GradingPeriod,
  SchoolYearsStore,
} from '@soovioo/shared';
import { todayString } from '../absences-page/excuse-days';
import { Loading } from '../loading/loading';
import { SchoolYearWizard } from '../school-year-wizard/school-year-wizard';

/** Výuka → Školní rok: the school's years with their dates and periods. The
 * management founds the next one and changes the dates. */
@Component({
  selector: 'app-school-year-panel',
  imports: [Button, DateFormatPipe, Loading, SchoolYearWizard, Tag],
  templateUrl: './school-year-panel.html',
})
export class SchoolYearPanel {
  private readonly store = inject(SchoolYearsStore);
  protected readonly canEdit = inject(AuthApiService).can('SchoolYear');

  /** Newest first. */
  protected readonly years = computed(() => {
    const years = this.store.years();
    return years && [...years].reverse();
  });

  constructor() {
    this.store.ensure();
  }

  protected state(period: GradingPeriod): string {
    const today = todayString();
    return period.to < today
      ? 'skončilo'
      : period.from > today
        ? 'nezačalo'
        : 'probíhá';
  }
}
