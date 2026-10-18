import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { Message as PMessage } from '@openng/optimus-ui/message';
import { Select } from '@openng/optimus-ui/select';
import { Tag } from '@openng/optimus-ui/tag';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  BehaviorApiService,
  BehaviorClassification,
  BehaviorClassificationValue,
  BehaviorRecord,
  BehaviorRecordType,
  SchoolClass,
  SchoolClassesApiService,
  SchoolHalfYear,
  Student,
  StudentsApiService,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';

const RECORD_TYPE_LABELS: Record<BehaviorRecordType, string> = {
  Pochvala: 'Pochvala',
  Poznamka: 'Poznámka',
  NapomenutiTridnihoUcitele: 'Napomenutí třídního učitele',
  DutkaTridnihoUcitele: 'Důtka třídního učitele',
  NapomenutiReditele: 'Napomenutí ředitele školy',
  DutkaReditele: 'Důtka ředitele školy',
};

const RECORD_TYPE_SEVERITY: Record<
  BehaviorRecordType,
  'success' | 'info' | 'warn' | 'danger'
> = {
  Pochvala: 'success',
  Poznamka: 'info',
  NapomenutiTridnihoUcitele: 'warn',
  DutkaTridnihoUcitele: 'danger',
  NapomenutiReditele: 'warn',
  DutkaReditele: 'danger',
};

const CLASSIFICATION_LABELS: Record<BehaviorClassificationValue, string> = {
  VelmiDobre: 'Velmi dobré',
  Uspokojive: 'Uspokojivé',
  Neuspokojive: 'Neuspokojivé',
};

const CLASSIFICATION_SEVERITY: Record<
  BehaviorClassificationValue,
  'success' | 'warn' | 'danger'
> = {
  VelmiDobre: 'success',
  Uspokojive: 'warn',
  Neuspokojive: 'danger',
};

const HALF_YEAR_OPTIONS: { value: SchoolHalfYear; label: string }[] = [
  { value: 'First', label: '1. pololetí' },
  { value: 'Second', label: '2. pololetí' },
];

@Component({
  selector: 'app-behavior-page',
  imports: [
    AppIcon,
    Button,
    Card,
    DatePicker,
    Dialog,
    FormsModule,
    InputNumber,
    PMessage,
    Select,
    Tag,
    Textarea,
  ],
  templateUrl: './behavior-page.html',
})
export class BehaviorPage {
  private readonly behaviorApi = inject(BehaviorApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  protected readonly authApi = inject(AuthApiService);

  protected readonly records = signal<BehaviorRecord[]>([]);
  protected readonly classifications = signal<BehaviorClassification[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);

  protected readonly halfYearOptions = HALF_YEAR_OPTIONS;

  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );
  private readonly studentsById = computed(
    () => new Map(this.students().map((s) => [s.id, s])),
  );

  /** Classes where the current user is the exact ClassTeacherId — computed
   * client-side from the already-loaded school class list (same data
   * admin-page.ts uses for its own accessLevel check) rather than adding a
   * new endpoint just for this. Only meaningful for a Teacher; Admin's
   * options never depend on it. */
  private readonly myClassTeacherClassIds = computed(() => {
    const myId = this.authApi.getUserId();
    return new Set(
      this.schoolClasses()
        .filter((c) => c.classTeacherId === myId)
        .map((c) => c.id),
    );
  });

  protected readonly recordDialogVisible = signal(false);
  protected readonly classificationDialogVisible = signal(false);
  protected readonly createError = signal<string | null>(null);

  protected newStudentId: number | null = null;
  protected newType: BehaviorRecordType = 'Pochvala';
  protected newText = '';
  protected newDate = new Date();

  protected classStudentId: number | null = null;
  protected classSchoolYearStartYear = this.defaultSchoolYearStartYear();
  protected classHalfYear: SchoolHalfYear = this.defaultHalfYear();
  protected classValue: BehaviorClassificationValue = 'VelmiDobre';

  constructor() {
    this.reload();
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (schoolClasses) => this.schoolClasses.set(schoolClasses),
      error: (err) => console.error('getSchoolClasses failed', err),
    });
  }

  protected canManageBehavior(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  protected recordTypeLabel(type: BehaviorRecordType): string {
    return RECORD_TYPE_LABELS[type];
  }

  protected recordTypeSeverity(
    type: BehaviorRecordType,
  ): 'success' | 'info' | 'warn' | 'danger' {
    return RECORD_TYPE_SEVERITY[type];
  }

  protected classificationLabel(value: BehaviorClassificationValue): string {
    return CLASSIFICATION_LABELS[value];
  }

  protected classificationSeverity(
    value: BehaviorClassificationValue,
  ): 'success' | 'warn' | 'danger' {
    return CLASSIFICATION_SEVERITY[value];
  }

  protected halfYearLabel(halfYear: SchoolHalfYear): string {
    return halfYear === 'First' ? '1. pololetí' : '2. pololetí';
  }

  /** Mirrors the backend's CanRecordTypeAsync closely enough to hide
   * obviously-forbidden options — the backend remains the real gate (a
   * 403 is still handled on submit), this is purely for not offering a
   * choice that would just bounce. */
  protected availableRecordTypes(
    studentId: number | null,
  ): { value: BehaviorRecordType; label: string }[] {
    if (!this.canManageBehavior()) {
      // Parent/Student may never record anything — without this, every
      // role fell through to `base` below, which `canManageRecord` then
      // misread as "may delete Pochvala/Poznámka", showing a trash icon
      // that just 403'd on click.
      return [];
    }
    const base: { value: BehaviorRecordType; label: string }[] = [
      { value: 'Pochvala', label: RECORD_TYPE_LABELS.Pochvala },
      { value: 'Poznamka', label: RECORD_TYPE_LABELS.Poznamka },
    ];
    if (this.authApi.isAdmin()) {
      return [
        ...base,
        {
          value: 'NapomenutiTridnihoUcitele',
          label: RECORD_TYPE_LABELS.NapomenutiTridnihoUcitele,
        },
        {
          value: 'DutkaTridnihoUcitele',
          label: RECORD_TYPE_LABELS.DutkaTridnihoUcitele,
        },
        {
          value: 'NapomenutiReditele',
          label: RECORD_TYPE_LABELS.NapomenutiReditele,
        },
        { value: 'DutkaReditele', label: RECORD_TYPE_LABELS.DutkaReditele },
      ];
    }
    const student =
      studentId !== null ? this.studentsById().get(studentId) : undefined;
    if (student && this.myClassTeacherClassIds().has(student.schoolClassId)) {
      return [
        ...base,
        {
          value: 'NapomenutiTridnihoUcitele',
          label: RECORD_TYPE_LABELS.NapomenutiTridnihoUcitele,
        },
        {
          value: 'DutkaTridnihoUcitele',
          label: RECORD_TYPE_LABELS.DutkaTridnihoUcitele,
        },
      ];
    }
    return base;
  }

  /** Same idea as availableRecordTypes, for the classification value picker. */
  protected availableClassificationValues(
    studentId: number | null,
  ): { value: BehaviorClassificationValue; label: string }[] {
    if (this.authApi.isAdmin()) {
      return [
        { value: 'VelmiDobre', label: CLASSIFICATION_LABELS.VelmiDobre },
        { value: 'Uspokojive', label: CLASSIFICATION_LABELS.Uspokojive },
        { value: 'Neuspokojive', label: CLASSIFICATION_LABELS.Neuspokojive },
      ];
    }
    const student =
      studentId !== null ? this.studentsById().get(studentId) : undefined;
    if (student && this.myClassTeacherClassIds().has(student.schoolClassId)) {
      return [{ value: 'VelmiDobre', label: CLASSIFICATION_LABELS.VelmiDobre }];
    }
    return [];
  }

  /** Approximates the backend's delete rule the same way — real
   * enforcement still happens server-side. */
  protected canManageRecord(record: BehaviorRecord): boolean {
    return this.availableRecordTypes(record.studentId).some(
      (o) => o.value === record.type,
    );
  }

  protected openAddRecordDialog(): void {
    this.createError.set(null);
    this.newStudentId = this.students()[0]?.id ?? null;
    this.onNewStudentChange();
    this.newText = '';
    this.newDate = new Date();
    this.recordDialogVisible.set(true);
  }

  /** Re-picking the student can narrow the type options (see
   * availableRecordTypes) — reset the type if it's no longer offered. */
  protected onNewStudentChange(): void {
    const options = this.availableRecordTypes(this.newStudentId);
    if (!options.some((o) => o.value === this.newType)) {
      this.newType = options[0]?.value ?? 'Pochvala';
    }
  }

  protected saveRecord(): void {
    if (!this.newStudentId || !this.newText.trim()) {
      return;
    }
    this.createError.set(null);
    this.behaviorApi
      .addBehaviorRecord({
        studentId: this.newStudentId,
        type: this.newType,
        text: this.newText,
        date: this.toDateOnlyString(this.newDate),
      })
      .subscribe({
        next: () => {
          this.recordDialogVisible.set(false);
          this.reload();
        },
        error: (err) => {
          console.error('addBehaviorRecord failed', err);
          this.createError.set(
            err.status === 403
              ? 'Nemáte oprávnění zapsat tento typ záznamu.'
              : 'Zápis se nezdařil.',
          );
        },
      });
  }

  protected deleteRecord(record: BehaviorRecord): void {
    this.behaviorApi.deleteBehaviorRecord(record.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('deleteBehaviorRecord failed', err),
    });
  }

  /** Shown in the trash icon's old slot for Parent/Student, who may never
   * delete a record — this is the only action they have on one. */
  protected markRead(record: BehaviorRecord): void {
    this.behaviorApi.markRecordRead(record.id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('markRecordRead failed', err),
    });
  }

  protected openSetClassificationDialog(): void {
    this.createError.set(null);
    this.classStudentId = this.students()[0]?.id ?? null;
    this.classSchoolYearStartYear = this.defaultSchoolYearStartYear();
    this.classHalfYear = this.defaultHalfYear();
    this.onClassStudentChange();
    this.classificationDialogVisible.set(true);
  }

  protected onClassStudentChange(): void {
    const options = this.availableClassificationValues(this.classStudentId);
    if (!options.some((o) => o.value === this.classValue)) {
      this.classValue = options[0]?.value ?? 'VelmiDobre';
    }
  }

  protected saveClassification(): void {
    if (!this.classStudentId) {
      return;
    }
    this.createError.set(null);
    this.behaviorApi
      .setClassification({
        studentId: this.classStudentId,
        schoolYearStartYear: this.classSchoolYearStartYear,
        halfYear: this.classHalfYear,
        value: this.classValue,
      })
      .subscribe({
        next: () => {
          this.classificationDialogVisible.set(false);
          this.reload();
        },
        error: (err) => {
          console.error('setClassification failed', err);
          this.createError.set(
            err.status === 403
              ? 'Nemáte oprávnění nastavit tuto klasifikaci.'
              : 'Nastavení se nezdařilo.',
          );
        },
      });
  }

  /** "YYYY-MM-DD" (DateOnly on the backend) → "DD.MM.YYYY" — string-only,
   * same reasoning as homework-page.ts's formatDueDate. */
  protected formatDate(date: string): string {
    const [year, month, day] = date.split('-');
    return `${day}.${month}.${year}`;
  }

  private defaultSchoolYearStartYear(): number {
    const now = new Date();
    return now.getMonth() + 1 >= 8 ? now.getFullYear() : now.getFullYear() - 1;
  }

  private defaultHalfYear(): SchoolHalfYear {
    const month = new Date().getMonth() + 1;
    return month >= 9 || month === 1 ? 'First' : 'Second';
  }

  /** Local (not UTC) y-m-d — see absences-page.ts's identical helper. */
  private toDateOnlyString(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private reload(): void {
    this.behaviorApi.getBehaviorRecords().subscribe({
      next: (records) => this.records.set(records),
      error: (err) => console.error('getBehaviorRecords failed', err),
    });
    this.behaviorApi.getBehaviorClassifications().subscribe({
      next: (classifications) => this.classifications.set(classifications),
      error: (err) => console.error('getBehaviorClassifications failed', err),
    });
  }
}
