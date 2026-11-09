import { Component, effect, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Select } from '@openng/optimus-ui/select';
import { ReportCardApiService } from '@soovioo/shared';
import { FloatField } from '../forms';

export interface StudentOption {
  id: number;
  label: string;
}

/** Vysvědčení in Známky: pick a pupil (when there is more than one) and
 * download their report card as a PDF. */
@Component({
  selector: 'app-report-card-tab',
  imports: [FloatField, FormsModule, Button, Select],
  templateUrl: './report-card-tab.html',
})
export class ReportCardTab {
  private readonly reportCardApi = inject(ReportCardApiService);

  readonly students = input.required<StudentOption[]>();

  protected readonly studentId = signal<number | null>(null);

  constructor() {
    effect(() => {
      if (this.studentId() === null) {
        this.studentId.set(this.students()[0]?.id ?? null);
      }
    });
  }

  protected download(): void {
    const studentId = this.studentId();
    if (!studentId) {
      return;
    }
    this.reportCardApi.downloadReportCard(studentId).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = 'vysvedceni.pdf';
        link.click();
        URL.revokeObjectURL(url);
      },
      error: (err) => console.error('downloadReportCard failed', err),
    });
  }
}
