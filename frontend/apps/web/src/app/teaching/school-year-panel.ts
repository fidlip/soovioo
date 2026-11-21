import { Component, computed, inject, signal } from '@angular/core';
import { Button } from '@openng/optimus-ui/button';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AuthApiService,
  DateFormatPipe,
  GradingPeriod,
  SchoolClass,
  SchoolClassesApiService,
  SchoolYear,
  SchoolYearsStore,
} from '@soovioo/shared';
import { todayString } from '../absences-page/excuse-days';
import { Loading } from '@fidlip/soovioo-ui';
import {
  DIRECTORS_DAYS,
  SchoolYearWizard,
  weekdays,
} from '../school-year-wizard/school-year-wizard';

/** Výuka → Školní rok: the school's years with their dates, periods and
 * days off. The management founds the next one and changes them. */
function parseDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day);
}

@Component({
  selector: 'app-school-year-panel',
  imports: [Button, DateFormatPipe, Loading, SchoolYearWizard, Tag],
  templateUrl: './school-year-panel.html',
})
export class SchoolYearPanel {
  private readonly store = inject(SchoolYearsStore);
  protected readonly canEdit = inject(AuthApiService).can('SchoolYear');
  protected readonly directorsDaysLimit = DIRECTORS_DAYS;
  private readonly classes = signal<SchoolClass[]>([]);

  /** Newest first. */
  protected readonly years = computed(() => {
    const years = this.store.years();
    return years && [...years].reverse();
  });

  constructor() {
    this.store.ensure();
    inject(SchoolClassesApiService)
      .getSchoolClasses()
      .subscribe({
        next: (classes) => this.classes.set(classes),
        error: (err) => console.error('getSchoolClasses failed', err),
      });
  }

  protected directorsDays(year: SchoolYear): number {
    return year.daysOff
      .filter((d) => d.kind === 'DirectorsDay')
      .reduce(
        (sum, d) => sum + weekdays(parseDate(d.from), parseDate(d.to)),
        0,
      );
  }

  protected classNames(ids: number[]): string {
    return this.classes()
      .filter((c) => ids.includes(c.id))
      .map((c) => c.name)
      .join(', ');
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
