import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Password } from '@openng/optimus-ui/password';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AppUser,
  AuthApiService,
  CreateUserRequest,
  Gender,
  MatrikaApiService,
  MySchoolApiService,
  ParentStudentLink,
  ParentStudentsApiService,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  UsersApiService,
} from '@sova/shared';

const ROLE_OPTIONS = [
  { label: 'Admin', value: 'Admin' },
  { label: 'Teacher', value: 'Teacher' },
  { label: 'Parent', value: 'Parent' },
];

const GENDER_OPTIONS: { label: string; value: Gender }[] = [
  { label: 'Chlapec', value: 'Male' },
  { label: 'Dívka', value: 'Female' },
];

@Component({
  selector: 'app-admin-page',
  imports: [
    FormsModule,
    Button,
    Card,
    InputNumber,
    InputText,
    Message,
    Password,
    PrimeTemplate,
    Select,
    TableModule,
    Tag,
  ],
  templateUrl: './admin-page.html',
})
export class AdminPage {
  private readonly authApi = inject(AuthApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly parentStudentsApi = inject(ParentStudentsApiService);
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly matrikaApi = inject(MatrikaApiService);

  protected readonly students = signal<Student[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly users = signal<AppUser[]>([]);
  protected readonly links = signal<ParentStudentLink[]>([]);

  protected readonly roleOptions = ROLE_OPTIONS;
  protected readonly genderOptions = GENDER_OPTIONS;

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  protected newSchoolClassName = '';
  protected newSchoolClassRocnik: number | null = null;
  protected newSubjectName = '';

  protected newStudentFirstName = '';
  protected newStudentLastName = '';
  protected newStudentSchoolClassId: number | null = null;
  protected newStudentRodneCislo = '';
  protected newStudentDateOfBirth = '';
  protected newStudentGender: Gender | null = null;

  protected newUsername = '';
  protected newPassword = '';
  protected newUserRole = 'Parent';

  protected newLinkUserId: number | null = null;
  protected newLinkStudentId: number | null = null;

  protected readonly matrikaIzo = signal('');
  protected readonly matrikaRedIzo = signal('');
  protected matrikaContactName = '';
  protected matrikaContactPhone = '';
  protected matrikaContactEmail = '';
  protected matrikaCast = '01';
  protected readonly matrikaExportErrors = signal<string[]>([]);

  protected error: string | null = null;

  constructor() {
    this.reloadAll();
    this.loadMySchool();
  }

  protected isAdmin(): boolean {
    return this.authApi.isAdmin();
  }

  private reloadAll(): void {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.newStudentSchoolClassId ??= students[0]?.schoolClassId ?? null;
      },
      error: (err) => console.error('getStudents failed', err),
    });
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (schoolClasses) => {
        this.schoolClasses.set(schoolClasses);
        this.newStudentSchoolClassId ??= schoolClasses[0]?.id ?? null;
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => this.subjects.set(subjects),
      error: (err) => console.error('getSubjects failed', err),
    });
    this.usersApi.getUsers().subscribe({
      next: (users) => {
        this.users.set(users);
        this.newLinkUserId ??= users[0]?.id ?? null;
      },
      error: (err) => console.error('getUsers failed', err),
    });
    this.parentStudentsApi.getLinks().subscribe({
      next: (links) => this.links.set(links),
      error: (err) => console.error('getLinks failed', err),
    });
  }

  protected addSchoolClass(): void {
    if (!this.newSchoolClassName.trim()) {
      return;
    }
    this.schoolClassesApi
      .addSchoolClass({ name: this.newSchoolClassName, rocnik: this.newSchoolClassRocnik ?? undefined })
      .subscribe({
        next: () => {
          this.newSchoolClassName = '';
          this.newSchoolClassRocnik = null;
          this.reloadAll();
        },
        error: (err) => this.showError('addSchoolClass failed', err),
      });
  }

  protected addSubject(): void {
    if (!this.newSubjectName.trim()) {
      return;
    }
    this.subjectsApi.addSubject({ name: this.newSubjectName }).subscribe({
      next: () => {
        this.newSubjectName = '';
        this.reloadAll();
      },
      error: (err) => this.showError('addSubject failed', err),
    });
  }

  protected addStudent(): void {
    if (!this.newStudentFirstName.trim() || !this.newStudentLastName.trim() || !this.newStudentSchoolClassId) {
      return;
    }
    this.studentsApi
      .addStudent({
        firstName: this.newStudentFirstName,
        lastName: this.newStudentLastName,
        schoolClassId: this.newStudentSchoolClassId,
        rodneCislo: this.newStudentRodneCislo || undefined,
        dateOfBirth: this.newStudentDateOfBirth || undefined,
        gender: this.newStudentGender ?? undefined,
      })
      .subscribe({
        next: () => {
          this.newStudentFirstName = '';
          this.newStudentLastName = '';
          this.newStudentRodneCislo = '';
          this.newStudentDateOfBirth = '';
          this.newStudentGender = null;
          this.reloadAll();
        },
        error: (err) => this.showError('addStudent failed', err),
      });
  }

  protected addUser(): void {
    if (!this.newUsername.trim() || !this.newPassword.trim()) {
      return;
    }
    const request: CreateUserRequest = {
      username: this.newUsername,
      password: this.newPassword,
      role: this.newUserRole,
    };
    this.usersApi.addUser(request).subscribe({
      next: () => {
        this.newUsername = '';
        this.newPassword = '';
        this.reloadAll();
      },
      error: (err) => this.showError('addUser failed', err),
    });
  }

  protected addLink(): void {
    if (!this.newLinkUserId || !this.newLinkStudentId) {
      return;
    }
    this.parentStudentsApi
      .addLink({ userId: this.newLinkUserId, studentId: this.newLinkStudentId })
      .subscribe({
        next: () => this.reloadAll(),
        error: (err) => this.showError('addLink failed', err),
      });
  }

  protected removeLink(link: ParentStudentLink): void {
    this.parentStudentsApi.removeLink(link.userId, link.studentId).subscribe({
      next: () => this.reloadAll(),
      error: (err) => this.showError('removeLink failed', err),
    });
  }

  private loadMySchool(): void {
    this.mySchoolApi.getMySchool().subscribe({
      next: (school) => {
        this.matrikaIzo.set(school.izo ?? '');
        this.matrikaRedIzo.set(school.redIzo ?? '');
      },
      error: (err) => console.error('getMySchool failed', err),
    });
  }

  protected saveMatrikaIds(): void {
    this.mySchoolApi
      .setMatrikaIds({ izo: this.matrikaIzo() || null, redIzo: this.matrikaRedIzo() || null })
      .subscribe({
        next: () => this.loadMySchool(),
        error: (err) => this.showError('setMatrikaIds failed', err),
      });
  }

  /** Pre-pilot skeleton export — see MatrikaExportService for what's (not)
   * implemented yet. Shows per-student validation errors instead of a
   * partial file when the data isn't complete enough to export. */
  protected exportMatrika(): void {
    this.matrikaExportErrors.set([]);

    const missing: string[] = [];
    if (!this.matrikaContactName.trim()) {
      missing.push('Chybí kontaktní osoba.');
    }
    if (!this.matrikaContactPhone.trim()) {
      missing.push('Chybí telefon na kontaktní osobu.');
    }
    if (!this.matrikaContactEmail.trim()) {
      missing.push('Chybí e-mail na kontaktní osobu.');
    }
    if (!this.matrikaCast.trim()) {
      missing.push('Chybí číslo části (CAST).');
    }
    if (missing.length > 0) {
      this.matrikaExportErrors.set(missing);
      return;
    }

    this.matrikaApi
      .exportBaseFile({
        cast: this.matrikaCast,
        contactName: this.matrikaContactName,
        contactPhone: this.matrikaContactPhone,
        contactEmail: this.matrikaContactEmail,
      })
      .subscribe({
        next: (response) => {
          const blob = response.body;
          if (!blob) {
            return;
          }
          const fileName = this.extractFileName(response.headers.get('Content-Disposition')) ?? 'matrika-export.xml';
          const url = URL.createObjectURL(blob);
          const link = document.createElement('a');
          link.href = url;
          link.download = fileName;
          link.click();
          URL.revokeObjectURL(url);
        },
        error: (err) => this.handleMatrikaExportError(err),
      });
  }

  private extractFileName(contentDisposition: string | null): string | null {
    const match = contentDisposition?.match(/filename="?([^";]+)"?/);
    return match?.[1] ?? null;
  }

  private handleMatrikaExportError(err: unknown): void {
    const httpError = err as { error?: Blob };
    if (!(httpError.error instanceof Blob)) {
      this.matrikaExportErrors.set(['Export se nepovedl.']);
      return;
    }
    httpError.error
      .text()
      .then((text) => {
        const parsed = JSON.parse(text) as { errors?: string[] | Record<string, string[]> };
        this.matrikaExportErrors.set(this.flattenErrors(parsed.errors) ?? ['Export se nepovedl.']);
      })
      .catch(() => this.matrikaExportErrors.set(['Export se nepovedl.']));
  }

  /** Our own 400s send `{ errors: string[] }`; ASP.NET's automatic model
   * validation (e.g. a required query param left empty) sends
   * `{ errors: { field: string[] } }` instead — normalize both to a flat list. */
  private flattenErrors(errors: string[] | Record<string, string[]> | undefined): string[] | null {
    if (!errors) {
      return null;
    }
    return Array.isArray(errors) ? errors : Object.values(errors).flat();
  }

  private showError(context: string, err: unknown): void {
    console.error(context, err);
    this.error = (err as { error?: string })?.error ?? 'Něco se nepovedlo.';
  }
}
