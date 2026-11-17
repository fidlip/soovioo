import { Component, inject, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import {
  MySchoolApiService,
  ReportCardMarkFormat,
  School,
  SchoolSetupRequest,
  SchoolType,
  SchoolYearsApiService,
  formatBankAccount,
} from '@soovioo/shared';
import { FORM_FIELDS, FloatField } from '../forms';
import { NotifyService } from '../notify.service';

type SetupStep = 'type' | 'seat' | 'contact' | 'reportCards' | 'ids';

const STEPS: SetupStep[] = ['type', 'seat', 'contact', 'reportCards', 'ids'];

/** Which step asks for a field, so a server error on it shows there. */
const STEP_OF: Record<string, SetupStep> = {
  type: 'type',
  name: 'seat',
  street: 'seat',
  city: 'seat',
  postalCode: 'seat',
  district: 'seat',
  directorName: 'contact',
  email: 'contact',
  phone: 'contact',
  reportCardMarkFormat: 'reportCards',
  izo: 'ids',
  redIzo: 'ids',
  bankAccountNumber: 'ids',
};

/** "Nastavení školy": a new school's admin goes through the school's
 * details step by step, as in founding a demo — all saved at the end, then
 * the school counts as set up. Every step stays rendered, so its fields
 * keep their values and a server error lands on the right one. */
@Component({
  selector: 'app-school-setup-wizard',
  imports: [
    Button,
    Dialog,
    ...FORM_FIELDS,
    FloatField,
    FormsModule,
    InputText,
    Select,
  ],
  templateUrl: './school-setup-wizard.html',
})
export class SchoolSetupWizard {
  private readonly api = inject(MySchoolApiService);
  private readonly schoolYearsApi = inject(SchoolYearsApiService);
  private readonly notify = inject(NotifyService);

  readonly completed = output<void>();

  protected readonly steps = STEPS;
  protected readonly visible = signal(false);
  protected readonly step = signal<SetupStep>('type');
  protected readonly saving = signal(false);
  protected readonly districts = signal<string[]>([]);

  protected readonly type = signal<SchoolType>('Elementary');
  protected readonly markFormat = signal<ReportCardMarkFormat>('Words');
  protected name = '';
  protected street = '';
  protected city = '';
  protected postalCode = '';
  protected district: string | null = null;
  protected directorName = '';
  protected email = '';
  protected phone = '';
  protected izo = '';
  protected redIzo = '';
  protected bankAccountNumber = '';

  open(school: School): void {
    this.name = school.name;
    this.type.set(school.type);
    this.street = school.street ?? '';
    this.city = school.city ?? '';
    this.postalCode = school.postalCode ?? '';
    this.district = school.district;
    this.directorName = school.directorName ?? '';
    this.email = school.email ?? '';
    this.phone = school.phone ?? '';
    this.izo = school.izo ?? '';
    this.redIzo = school.redIzo ?? '';
    this.bankAccountNumber = school.bankAccountNumber
      ? formatBankAccount(school.bankAccountNumber)
      : '';
    this.api.getReportCardSettings().subscribe({
      next: (settings) => this.markFormat.set(settings.markFormat),
      error: (err) => console.error('getReportCardSettings failed', err),
    });
    if (this.districts().length === 0) {
      this.schoolYearsApi.getDistricts().subscribe({
        next: (districts) => this.districts.set(districts),
        error: (err) => console.error('getDistricts failed', err),
      });
    }
    this.step.set('type');
    this.visible.set(true);
  }

  protected stepNumber(step: SetupStep): number {
    return STEPS.indexOf(step) + 1;
  }

  protected chooseType(type: SchoolType): void {
    this.type.set(type);
    this.next();
  }

  protected chooseMarkFormat(format: ReportCardMarkFormat): void {
    this.markFormat.set(format);
    this.next();
  }

  protected next(): void {
    const index = STEPS.indexOf(this.step());
    if (index < STEPS.length - 1) {
      this.step.set(STEPS[index + 1]);
    }
  }

  protected back(): void {
    const index = STEPS.indexOf(this.step());
    if (index > 0) {
      this.step.set(STEPS[index - 1]);
    }
  }

  protected save(): void {
    this.saving.set(true);
    this.api.completeSetup(this.request()).subscribe({
      next: () => {
        this.saving.set(false);
        this.visible.set(false);
        this.completed.emit();
      },
      error: (err) => {
        this.saving.set(false);
        const fields = Object.keys(
          (err as { error?: { errors?: Record<string, string[]> } })?.error
            ?.errors ?? {},
        );
        const step = STEPS.find((s) => fields.some((f) => STEP_OF[f] === s));
        if (step) {
          this.step.set(step);
        }
        this.notify.actionFailed('completeSetup failed', err);
      },
    });
  }

  private request(): SchoolSetupRequest {
    const text = (value: string) => value.trim() || null;
    return {
      name: this.name.trim(),
      type: this.type(),
      reportCardMarkFormat: this.markFormat(),
      street: text(this.street),
      city: text(this.city),
      postalCode: text(this.postalCode),
      district: this.district,
      directorName: text(this.directorName),
      email: text(this.email),
      phone: text(this.phone),
      izo: text(this.izo),
      redIzo: text(this.redIzo),
      bankAccountNumber: text(this.bankAccountNumber),
    };
  }
}
