import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { MainBlockApiService } from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';

/** The school's hlavní výukový blok; grades' and classes' are in Učební
 * plán and Třídy, per version. */
@Component({
  selector: 'app-main-block-settings',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    Button,
    Card,
    InputNumber,
    InfoHint,
  ],
  templateUrl: './main-block-settings.html',
})
export class MainBlockSettings {
  private readonly api = inject(MainBlockApiService);
  private readonly notify = inject(NotifyService);

  protected from: number | null = 1;
  protected to: number | null = 4;

  constructor() {
    this.api.get().subscribe({
      next: (b) => {
        this.from = b.from;
        this.to = b.to;
      },
      error: (err) => this.notify.actionFailed('get main block failed', err),
    });
  }

  protected save(): void {
    if (this.from === null || this.to === null) {
      this.notify.error('Vyplňte, od které do které hodiny blok je.');
      return;
    }
    if (this.to < this.from) {
      this.notify.error('Blok nemůže končit dřív, než začíná.');
      return;
    }
    this.api.set({ from: this.from, to: this.to }).subscribe({
      next: () => this.notify.success('Uloženo.'),
      error: (err) => this.notify.actionFailed('set main block failed', err),
    });
  }
}
