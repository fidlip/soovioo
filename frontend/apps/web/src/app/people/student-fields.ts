import { Component, inject, input, signal } from '@angular/core';
import { ControlContainer, FormsModule, NgForm } from '@angular/forms';
import {
  AutoComplete,
  AutoCompleteCompleteEvent,
} from '@openng/optimus-ui/autocomplete';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Select } from '@openng/optimus-ui/select';
import {
  Country,
  Gender,
  Municipality,
  ReferenceApiService,
  SchoolClass,
  Student,
  StudentMatrika,
} from '@soovioo/shared';
import { FloatField } from '../forms';

export const GENDER_OPTIONS: { label: string; value: Gender }[] = [
  { label: 'Chlapec', value: 'Male' },
  { label: 'Dívka', value: 'Female' },
];

/** RAKO kvalifikátor státního občanství — only 8 stable rows, so a hardcoded
 * list rather than a DB table. See StudentProfile.CitizenshipQualifier. */
const CITIZENSHIP_QUALIFIER_OPTIONS = [
  { label: 'Občan ČR', value: '3' },
  { label: 'Cizinec s trvalým pobytem v ČR', value: '5' },
  { label: 'Cizinec bez trvalého pobytu v ČR', value: '6' },
  { label: 'Azylant nebo žadatel o azyl', value: 'A' },
  { label: 'Osoba s dočasnou ochranou v ČR', value: 'D' },
  { label: 'Osoba s doplňkovou ochranou v ČR', value: 'K' },
  { label: 'Osoba bez státního občanství', value: '0' },
  { label: 'Občanství neznámé/neudané', value: '9' },
];

/** What the matrika fields edit — dates as Date for the pickers. */
export interface StudentForm {
  schoolClassId: number | null;
  rodneCislo: string;
  dateOfBirth: Date | null;
  gender: Gender | null;
  citizenshipQualifier: string | null;
  countryCode: string | null;
  street: string;
  municipality: Municipality | null;
  postalCode: string;
  catchmentArea: boolean;
  enrollmentDate: Date | null;
  contactPhone: string;
  contactEmail: string;
  defaultVariableSymbol: string;
}

export function emptyStudentForm(schoolClassId: number | null): StudentForm {
  return {
    schoolClassId,
    rodneCislo: '',
    dateOfBirth: null,
    gender: null,
    citizenshipQualifier: null,
    countryCode: null,
    street: '',
    municipality: null,
    postalCode: '',
    catchmentArea: false,
    enrollmentDate: null,
    contactPhone: '',
    contactEmail: '',
    defaultVariableSymbol: '',
  };
}

export function studentFormOf(student: Student): StudentForm {
  return {
    schoolClassId: student.schoolClassId,
    rodneCislo: student.rodneCislo ?? '',
    dateOfBirth: student.dateOfBirth ? new Date(student.dateOfBirth) : null,
    gender: student.gender,
    citizenshipQualifier: student.citizenshipQualifier,
    countryCode: student.countryCode,
    street: student.street ?? '',
    municipality: student.municipalityCode
      ? {
          code: student.municipalityCode,
          name: student.municipalityName ?? student.municipalityCode,
          districtCode: '',
        }
      : null,
    postalCode: student.postalCode ?? '',
    catchmentArea: student.catchmentArea ?? false,
    enrollmentDate: student.enrollmentDate
      ? new Date(student.enrollmentDate)
      : null,
    contactPhone: student.contactPhone ?? '',
    contactEmail: student.contactEmail ?? '',
    defaultVariableSymbol: student.defaultVariableSymbol ?? '',
  };
}

export function matrikaOf(form: StudentForm): StudentMatrika {
  return {
    // The field is required — 0 never gets past the form (nor the server).
    schoolClassId: form.schoolClassId ?? 0,
    rodneCislo: form.rodneCislo || undefined,
    dateOfBirth: form.dateOfBirth ? toDateOnly(form.dateOfBirth) : undefined,
    gender: form.gender ?? undefined,
    citizenshipQualifier: form.citizenshipQualifier || undefined,
    countryCode: form.countryCode || undefined,
    street: form.street || undefined,
    municipalityCode: form.municipality?.code || undefined,
    postalCode: form.postalCode || undefined,
    catchmentArea: form.catchmentArea || undefined,
    enrollmentDate: form.enrollmentDate
      ? toDateOnly(form.enrollmentDate)
      : undefined,
    contactPhone: form.contactPhone || undefined,
    contactEmail: form.contactEmail || undefined,
    defaultVariableSymbol: form.defaultVariableSymbol || undefined,
  };
}

function toDateOnly(date: Date): string {
  const month = `${date.getMonth() + 1}`.padStart(2, '0');
  const day = `${date.getDate()}`.padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

/** A pupil's matrika fields (everything but the name), inside the parent's
 * form — used by Žáci (class teacher) and the person dialog. */
@Component({
  selector: 'app-student-fields',
  imports: [
    FloatField,
    AutoComplete,
    Checkbox,
    DatePicker,
    FormsModule,
    InputText,
    Select,
  ],
  viewProviders: [{ provide: ControlContainer, useExisting: NgForm }],
  host: { class: 'flex flex-wrap items-end gap-2' },
  template: `
    @let f = form();
    <app-float-field label="Třída" for="studentSchoolClassId">
      <p-select
        inputId="studentSchoolClassId"
        [options]="classes()"
        optionLabel="name"
        optionValue="id"
        ariaLabel="Třída"
        [(ngModel)]="f.schoolClassId"
        name="schoolClassId"
        required
        styleClass="w-32"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="Datum narození" for="studentDateOfBirth">
      <p-datePicker
        inputId="studentDateOfBirth"
        [(ngModel)]="f.dateOfBirth"
        name="studentDateOfBirth"
        [showIcon]="true"
        [showClear]="true"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="Pohlaví" for="studentGender">
      <p-select
        ariaLabel="Pohlaví"
        inputId="studentGender"
        [options]="genderOptions"
        optionLabel="label"
        optionValue="value"
        [(ngModel)]="f.gender"
        name="studentGender"
        styleClass="w-36"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="Rodné číslo" for="studentRodneCislo">
      <input
        id="studentRodneCislo"
        pInputText
        type="text"
        name="studentRodneCislo"
        [(ngModel)]="f.rodneCislo"
      />
    </app-float-field>
    <app-float-field
      label="Kvalifikátor občanství"
      for="studentCitizenshipQualifier"
    >
      <p-select
        ariaLabel="Kvalifikátor občanství"
        inputId="studentCitizenshipQualifier"
        [options]="citizenshipQualifierOptions"
        optionLabel="label"
        optionValue="value"
        name="studentCitizenshipQualifier"
        [(ngModel)]="f.citizenshipQualifier"
        [filter]="true"
        styleClass="w-56"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="Stát" for="studentCountryCode">
      <p-select
        ariaLabel="Stát"
        inputId="studentCountryCode"
        [options]="countries()"
        optionLabel="name"
        optionValue="code"
        name="studentCountryCode"
        [(ngModel)]="f.countryCode"
        [filter]="true"
        styleClass="w-36"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="Ulice" for="studentStreet">
      <input
        id="studentStreet"
        pInputText
        type="text"
        name="studentStreet"
        [(ngModel)]="f.street"
      />
    </app-float-field>
    <app-float-field label="Obec" for="studentMunicipality">
      <p-autoComplete
        inputId="studentMunicipality"
        [(ngModel)]="f.municipality"
        name="studentMunicipality"
        [suggestions]="municipalitySuggestions()"
        (completeMethod)="searchMunicipalities($event)"
        optionLabel="name"
        dataKey="code"
        styleClass="w-36"
        inputStyleClass="w-full"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field label="PSČ" for="studentPostalCode">
      <input
        id="studentPostalCode"
        pInputText
        type="text"
        name="studentPostalCode"
        [(ngModel)]="f.postalCode"
        class="w-20"
      />
    </app-float-field>
    <label
      class="flex items-center gap-1.5 text-sm text-surface-600 dark:text-surface-300"
    >
      <p-checkbox
        [binary]="true"
        [(ngModel)]="f.catchmentArea"
        name="studentCatchmentArea"
      />
      Spádová oblast
    </label>
    <app-float-field label="Datum nástupu" for="studentEnrollmentDate">
      <p-datePicker
        inputId="studentEnrollmentDate"
        [(ngModel)]="f.enrollmentDate"
        name="studentEnrollmentDate"
        [showIcon]="true"
        [showClear]="true"
        appendTo="body"
      />
    </app-float-field>
    <app-float-field
      label="Nouzový kontakt — telefon"
      for="studentContactPhone"
    >
      <input
        id="studentContactPhone"
        pInputText
        type="tel"
        name="studentContactPhone"
        [(ngModel)]="f.contactPhone"
      />
    </app-float-field>
    <app-float-field label="Nouzový kontakt — e-mail" for="studentContactEmail">
      <input
        id="studentContactEmail"
        pInputText
        type="email"
        name="studentContactEmail"
        [(ngModel)]="f.contactEmail"
      />
    </app-float-field>
    <app-float-field label="Variabilní symbol" for="defaultVariableSymbol">
      <input
        id="defaultVariableSymbol"
        pInputText
        type="text"
        placeholder="prázdné = vygenerovat"
        name="defaultVariableSymbol"
        [(ngModel)]="f.defaultVariableSymbol"
      />
    </app-float-field>
  `,
})
export class StudentFields {
  private readonly referenceApi = inject(ReferenceApiService);

  /** Edited in place. */
  readonly form = input.required<StudentForm>();
  readonly classes = input<SchoolClass[]>([]);
  readonly countries = input<Country[]>([]);

  protected readonly genderOptions = GENDER_OPTIONS;
  protected readonly citizenshipQualifierOptions =
    CITIZENSHIP_QUALIFIER_OPTIONS;
  protected readonly municipalitySuggestions = signal<Municipality[]>([]);

  protected searchMunicipalities(event: AutoCompleteCompleteEvent): void {
    this.referenceApi.searchMunicipalities(event.query).subscribe({
      next: (results) => this.municipalitySuggestions.set(results),
      error: (err) => console.error('searchMunicipalities failed', err),
    });
  }
}
