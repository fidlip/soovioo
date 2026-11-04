import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { TeacherWishesApiService } from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** The school's default wishes of teachers for the timetable generator —
 * a teacher's own (Osoby) take their place. */
@Component({
  selector: 'app-teacher-wishes-defaults',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    InputNumber,
    InfoHint,
  ],
  templateUrl: './teacher-wishes-defaults.html',
})
export class TeacherWishesDefaults {
  private readonly wishesApi = inject(TeacherWishesApiService);
  private readonly notify = inject(NotifyService);

  protected minLessonsPerDay: number | null = null;
  protected maxLessonsPerDay: number | null = null;
  protected maxGapsPerDay: number | null = null;

  constructor() {
    this.wishesApi.get().subscribe({
      next: (w) => {
        this.minLessonsPerDay = w.minLessonsPerDay;
        this.maxLessonsPerDay = w.maxLessonsPerDay;
        this.maxGapsPerDay = w.maxGapsPerDay;
      },
      error: (err) =>
        this.notify.actionFailed('get teacher wishes failed', err),
    });
  }

  protected save(): void {
    this.wishesApi
      .set({
        minLessonsPerDay: this.minLessonsPerDay,
        maxLessonsPerDay: this.maxLessonsPerDay,
        maxGapsPerDay: this.maxGapsPerDay,
      })
      .subscribe({
        next: () => this.notify.success('Uloženo.'),
        error: (err) =>
          this.notify.actionFailed('set teacher wishes failed', err),
      });
  }
}
