import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import {
  AssessmentMode,
  AssessmentModeRule,
  MySchoolApiService,
  ReportCardMarkFormat,
  SchoolClassesApiService,
  StudentsApiService,
  SubjectsApiService,
} from '@soovioo/shared';
import { FloatField } from '../forms';
import { InfoHint } from '../info-hint/info-hint';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';

/** "Všechny předměty" and "Chování" among the subjects of a rule. */
const ALL_SUBJECTS = 'all';
const BEHAVIOR = 'behavior';

const MODE_OPTIONS: { value: AssessmentMode; label: string }[] = [
  { value: 'Verbal', label: 'Slovní hodnocení' },
  {
    value: 'ClassificationWithVerbal',
    label: 'Klasifikace doplněná slovním hodnocením',
  },
  { value: 'Classification', label: 'Klasifikace' },
];

/** Vysvědčení in Údaje o škole: whether a stupeň goes on the report card as
 * "1" or "výborný", and the ročníky, classes and single pupils assessed or
 * written otherwise — the school sets both in its školní řád (§ 15 of
 * vyhláška 48/2005 Sb.). */
@Component({
  selector: 'app-report-card-settings',
  imports: [Button, Card, FloatField, FormsModule, InfoHint, Select, TermPipe],
  templateUrl: './report-card-settings.html',
})
export class ReportCardSettings {
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly classesApi = inject(SchoolClassesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly notify = inject(NotifyService);

  protected readonly markFormatOptions: {
    value: ReportCardMarkFormat;
    label: string;
  }[] = [
    { value: 'Digits', label: 'Číslem (1)' },
    { value: 'Words', label: 'Slovem (výborný)' },
  ];
  protected readonly ruleFormatOptions: {
    value: ReportCardMarkFormat | null;
    label: string;
  }[] = [{ value: null, label: 'Jako výchozí' }, ...this.markFormatOptions];
  protected readonly markFormat = signal<ReportCardMarkFormat>('Words');

  protected readonly modeOptions = MODE_OPTIONS;
  protected readonly rules = signal<AssessmentModeRule[]>([]);
  protected readonly targetOptions = [
    { value: 'rocnik', label: 'Ročník' },
    { value: 'class', label: 'Třída' },
    { value: 'student', label: 'Žák' },
  ];
  protected readonly target = signal<'rocnik' | 'class' | 'student'>('rocnik');
  protected readonly rocnik = signal<number | null>(null);
  protected readonly classId = signal<number | null>(null);
  protected readonly studentId = signal<number | null>(null);
  protected readonly subjectKey = signal<string>(ALL_SUBJECTS);
  protected readonly mode = signal<AssessmentMode>('Verbal');
  protected readonly ruleFormat = signal<ReportCardMarkFormat | null>(null);

  private readonly rocniky = signal<number[]>([]);
  protected readonly rocnikOptions = computed(() =>
    this.rocniky().map((r) => ({ value: r, label: `${r}. ročník` })),
  );
  protected readonly classOptions = signal<{ id: number; label: string }[]>([]);
  protected readonly studentOptions = signal<{ id: number; label: string }[]>(
    [],
  );
  protected readonly subjectOptions = signal<{ key: string; label: string }[]>([
    { key: ALL_SUBJECTS, label: 'Všechny předměty' },
    { key: BEHAVIOR, label: 'Chování' },
  ]);

  protected readonly canAdd = computed(() => {
    switch (this.target()) {
      case 'rocnik':
        return this.rocnik() !== null;
      case 'class':
        return this.classId() !== null;
      default:
        return this.studentId() !== null;
    }
  });

  constructor() {
    this.mySchoolApi.getReportCardSettings().subscribe({
      next: (settings) => this.markFormat.set(settings.markFormat),
      error: (err) => console.error('getReportCardSettings failed', err),
    });
    this.loadRules();
    this.classesApi.getSchoolClasses().subscribe({
      next: (classes) => {
        this.rocniky.set(
          [
            ...new Set(
              classes
                .map((c) => c.rocnik)
                .filter((r): r is number => r !== null),
            ),
          ].sort((a, b) => a - b),
        );
        this.classOptions.set(
          classes
            .map((c) => ({ id: c.id, label: c.name }))
            .sort((a, b) => a.label.localeCompare(b.label, 'cs')),
        );
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    this.studentsApi.getStudents().subscribe({
      next: (students) =>
        this.studentOptions.set(
          students
            .map((s) => ({
              id: s.id,
              label: `${s.lastName} ${s.firstName} (${s.schoolClassName})`,
            }))
            .sort((a, b) => a.label.localeCompare(b.label, 'cs')),
        ),
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) =>
        this.subjectOptions.update((options) => [
          ...options,
          ...subjects
            .filter((s) => !s.coveredByDivisions)
            .map((s) => ({ key: `${s.id}`, label: s.name })),
        ]),
      error: (err) => console.error('getSubjects failed', err),
    });
  }

  protected save(): void {
    this.mySchoolApi
      .setReportCardSettings({ markFormat: this.markFormat() })
      .subscribe({
        next: (settings) => {
          this.markFormat.set(settings.markFormat);
          this.notify.success('Uloženo.');
        },
        error: (err) =>
          this.notify.actionFailed('setReportCardSettings failed', err),
      });
  }

  protected ruleTarget(rule: AssessmentModeRule): string {
    return rule.rocnik !== null
      ? `${rule.rocnik}. ročník`
      : (rule.schoolClassName ?? rule.studentName ?? '');
  }

  protected ruleSubjects(rule: AssessmentModeRule): string {
    return rule.behavior ? 'chování' : (rule.subjectName ?? 'všechny předměty');
  }

  protected modeLabel(rule: AssessmentModeRule): string {
    const mode =
      MODE_OPTIONS.find((o) => o.value === rule.mode)?.label ?? rule.mode;
    const format = this.markFormatOptions.find(
      (o) => o.value === rule.markFormat,
    );
    return format ? `${mode}, ${format.label.toLowerCase()}` : mode;
  }

  protected addRule(): void {
    const key = this.subjectKey();
    const target = this.target();
    this.mySchoolApi
      .setAssessmentRule({
        rocnik: target === 'rocnik' ? this.rocnik() : null,
        schoolClassId: target === 'class' ? this.classId() : null,
        studentId: target === 'student' ? this.studentId() : null,
        subjectId:
          key === ALL_SUBJECTS || key === BEHAVIOR ? null : Number(key),
        behavior: key === BEHAVIOR,
        mode: this.mode(),
        markFormat: this.mode() === 'Verbal' ? null : this.ruleFormat(),
      })
      .subscribe({
        next: () => this.loadRules(),
        error: (err) =>
          this.notify.actionFailed('setAssessmentRule failed', err),
      });
  }

  protected deleteRule(rule: AssessmentModeRule): void {
    this.confirmation.confirm({
      header: 'Smazat výjimku',
      message: `Smazat výjimku „${this.ruleTarget(rule)}, ${this.ruleSubjects(rule)}“? Slovní hodnocení, která už učitelé napsali, zůstanou uložená.`,
      icon: 'pi pi-trash',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.mySchoolApi.deleteAssessmentRule(rule.id).subscribe({
          next: () => this.loadRules(),
          error: (err) =>
            this.notify.actionFailed('deleteAssessmentRule failed', err),
        }),
    });
  }

  private loadRules(): void {
    this.mySchoolApi.getAssessmentRules().subscribe({
      next: (rules) => this.rules.set(rules),
      error: (err) => console.error('getAssessmentRules failed', err),
    });
  }
}
