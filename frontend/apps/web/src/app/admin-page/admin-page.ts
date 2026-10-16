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
  UpdateUserRequest,
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

  /** Třídní učitel candidates — Teacher role only. */
  protected readonly teacherOptions = computed(() => this.users().filter((u) => u.role === 'Teacher'));

  protected newSchoolClassName = '';
  protected newSchoolClassRocnik: number | null = null;
  protected newSchoolClassTeacherId: number | null = null;
  protected newSchoolClassTeachingLanguage = '';
  protected newSubjectName = '';
  protected newSubjectAbbreviation = '';

  protected newStudentFirstName = '';
  protected newStudentLastName = '';
  protected newStudentSchoolClassId: number | null = null;
  protected newStudentRodneCislo = '';
  protected newStudentDateOfBirth = '';
  protected newStudentGender: Gender | null = null;
  protected newStudentCitizenship = '';
  protected newStudentStreet = '';
  protected newStudentCity = '';
  protected newStudentPostalCode = '';
  protected newStudentCatchmentArea = false;
  protected newStudentEnrollmentDate = '';
  protected newStudentContactPhone = '';
  protected newStudentContactEmail = '';

  protected newUsername = '';
  protected newPassword = '';
  protected newUserRole = 'Parent';
  protected newUserPhone = '';
  protected newUserEmail = '';

  protected newLinkUserId: number | null = null;
  protected newLinkStudentId: number | null = null;

  protected editingSchoolClassId: number | null = null;
  protected editSchoolClassName = '';
  protected editSchoolClassRocnik: number | null = null;
  protected editSchoolClassTeacherId: number | null = null;
  protected editSchoolClassTeachingLanguage = '';

  protected editingSubjectId: number | null = null;
  protected editSubjectName = '';
  protected editSubjectAbbreviation = '';

  protected editingStudentId: number | null = null;
  protected editStudentFirstName = '';
  protected editStudentLastName = '';
  protected editStudentSchoolClassId: number | null = null;
  protected editStudentRodneCislo = '';
  protected editStudentDateOfBirth = '';
  protected editStudentGender: Gender | null = null;
  protected editStudentCitizenship = '';
  protected editStudentStreet = '';
  protected editStudentCity = '';
  protected editStudentPostalCode = '';
  protected editStudentCatchmentArea = false;
  protected editStudentEnrollmentDate = '';
  protected editStudentContactPhone = '';
  protected editStudentContactEmail = '';

  protected editingUserId: number | null = null;
  protected editUsername = '';
  protected editUserRole = 'Parent';
  protected editUserPassword = '';
  protected editUserPhone = '';
  protected editUserEmail = '';

  protected readonly schoolStreet = signal('');
  protected readonly schoolCity = signal('');
  protected readonly schoolPostalCode = signal('');
  protected readonly schoolDirectorName = signal('');
  protected readonly schoolEmail = signal('');
  protected readonly schoolPhone = signal('');

  protected readonly matrikaIzo = signal('');
  protected readonly matrikaRedIzo = signal('');
  protected matrikaContactName = '';
  protected matrikaContactPhone = '';
  protected matrikaContactEmail = '';
  protected matrikaCast = '01';
  protected readonly matrikaExportErrors = signal<string[]>([]);

  /** A signal, not a plain field — this app has no zone.js polyfill, so a
   * plain-field write inside an RxJS subscribe callback doesn't reliably
   * schedule change detection on its own (it only "works" by coincidence
   * when some other signal write happens to run in the same tick). */
  protected readonly error = signal<string | null>(null);

  constructor() {
    this.reloadAll();
    this.loadMySchool();
  }

  protected isAdmin(): boolean {
    return this.authApi.isAdmin();
  }

  protected classTeacherName(classTeacherId: number | null): string {
    if (classTeacherId === null) {
      return '—';
    }
    return this.users().find((u) => u.id === classTeacherId)?.username ?? '—';
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
      .addSchoolClass({
        name: this.newSchoolClassName,
        rocnik: this.newSchoolClassRocnik ?? undefined,
        classTeacherId: this.newSchoolClassTeacherId ?? undefined,
        teachingLanguage: this.newSchoolClassTeachingLanguage || undefined,
      })
      .subscribe({
        next: () => {
          this.newSchoolClassName = '';
          this.newSchoolClassRocnik = null;
          this.newSchoolClassTeacherId = null;
          this.newSchoolClassTeachingLanguage = '';
          this.reloadAll();
        },
        error: (err) => this.showError('addSchoolClass failed', err),
      });
  }

  protected startEditSchoolClass(schoolClass: SchoolClass): void {
    this.editingSchoolClassId = schoolClass.id;
    this.editSchoolClassName = schoolClass.name;
    this.editSchoolClassRocnik = schoolClass.rocnik;
    this.editSchoolClassTeacherId = schoolClass.classTeacherId;
    this.editSchoolClassTeachingLanguage = schoolClass.teachingLanguage ?? '';
  }

  protected cancelEditSchoolClass(): void {
    this.editingSchoolClassId = null;
  }

  protected saveEditSchoolClass(): void {
    if (this.editingSchoolClassId === null || !this.editSchoolClassName.trim()) {
      return;
    }
    this.schoolClassesApi
      .updateSchoolClass(this.editingSchoolClassId, {
        name: this.editSchoolClassName,
        rocnik: this.editSchoolClassRocnik ?? undefined,
        classTeacherId: this.editSchoolClassTeacherId ?? undefined,
        teachingLanguage: this.editSchoolClassTeachingLanguage || undefined,
      })
      .subscribe({
        next: () => {
          this.editingSchoolClassId = null;
          this.reloadAll();
        },
        error: (err) => this.showError('updateSchoolClass failed', err),
      });
  }

  protected deleteSchoolClass(schoolClass: SchoolClass): void {
    this.schoolClassesApi.deleteSchoolClass(schoolClass.id).subscribe({
      next: () => this.reloadAll(),
      error: (err) => this.showError('deleteSchoolClass failed', err),
    });
  }

  protected addSubject(): void {
    if (!this.newSubjectName.trim()) {
      return;
    }
    this.subjectsApi
      .addSubject({ name: this.newSubjectName, abbreviation: this.newSubjectAbbreviation || undefined })
      .subscribe({
        next: () => {
          this.newSubjectName = '';
          this.newSubjectAbbreviation = '';
          this.reloadAll();
        },
        error: (err) => this.showError('addSubject failed', err),
      });
  }

  protected startEditSubject(subject: Subject): void {
    this.editingSubjectId = subject.id;
    this.editSubjectName = subject.name;
    this.editSubjectAbbreviation = subject.abbreviation ?? '';
  }

  protected cancelEditSubject(): void {
    this.editingSubjectId = null;
  }

  protected saveEditSubject(): void {
    if (this.editingSubjectId === null || !this.editSubjectName.trim()) {
      return;
    }
    this.subjectsApi
      .updateSubject(this.editingSubjectId, {
        name: this.editSubjectName,
        abbreviation: this.editSubjectAbbreviation || undefined,
      })
      .subscribe({
        next: () => {
          this.editingSubjectId = null;
          this.reloadAll();
        },
        error: (err) => this.showError('updateSubject failed', err),
      });
  }

  protected deleteSubject(subject: Subject): void {
    this.subjectsApi.deleteSubject(subject.id).subscribe({
      next: () => this.reloadAll(),
      error: (err) => this.showError('deleteSubject failed', err),
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
        citizenship: this.newStudentCitizenship || undefined,
        street: this.newStudentStreet || undefined,
        city: this.newStudentCity || undefined,
        postalCode: this.newStudentPostalCode || undefined,
        catchmentArea: this.newStudentCatchmentArea || undefined,
        enrollmentDate: this.newStudentEnrollmentDate || undefined,
        contactPhone: this.newStudentContactPhone || undefined,
        contactEmail: this.newStudentContactEmail || undefined,
      })
      .subscribe({
        next: () => {
          this.newStudentFirstName = '';
          this.newStudentLastName = '';
          this.newStudentRodneCislo = '';
          this.newStudentDateOfBirth = '';
          this.newStudentGender = null;
          this.newStudentCitizenship = '';
          this.newStudentStreet = '';
          this.newStudentCity = '';
          this.newStudentPostalCode = '';
          this.newStudentCatchmentArea = false;
          this.newStudentEnrollmentDate = '';
          this.newStudentContactPhone = '';
          this.newStudentContactEmail = '';
          this.reloadAll();
        },
        error: (err) => this.showError('addStudent failed', err),
      });
  }

  protected startEditStudent(student: Student): void {
    this.editingStudentId = student.id;
    this.editStudentFirstName = student.firstName;
    this.editStudentLastName = student.lastName;
    this.editStudentSchoolClassId = student.schoolClassId;
    this.editStudentRodneCislo = student.rodneCislo ?? '';
    this.editStudentDateOfBirth = student.dateOfBirth ?? '';
    this.editStudentGender = student.gender;
    this.editStudentCitizenship = student.citizenship ?? '';
    this.editStudentStreet = student.street ?? '';
    this.editStudentCity = student.city ?? '';
    this.editStudentPostalCode = student.postalCode ?? '';
    this.editStudentCatchmentArea = student.catchmentArea ?? false;
    this.editStudentEnrollmentDate = student.enrollmentDate ?? '';
    this.editStudentContactPhone = student.contactPhone ?? '';
    this.editStudentContactEmail = student.contactEmail ?? '';
  }

  protected cancelEditStudent(): void {
    this.editingStudentId = null;
  }

  protected saveEditStudent(): void {
    if (
      this.editingStudentId === null ||
      !this.editStudentFirstName.trim() ||
      !this.editStudentLastName.trim() ||
      !this.editStudentSchoolClassId
    ) {
      return;
    }
    this.studentsApi
      .updateStudent(this.editingStudentId, {
        firstName: this.editStudentFirstName,
        lastName: this.editStudentLastName,
        schoolClassId: this.editStudentSchoolClassId,
        rodneCislo: this.editStudentRodneCislo || undefined,
        dateOfBirth: this.editStudentDateOfBirth || undefined,
        gender: this.editStudentGender ?? undefined,
        citizenship: this.editStudentCitizenship || undefined,
        street: this.editStudentStreet || undefined,
        city: this.editStudentCity || undefined,
        postalCode: this.editStudentPostalCode || undefined,
        catchmentArea: this.editStudentCatchmentArea || undefined,
        enrollmentDate: this.editStudentEnrollmentDate || undefined,
        contactPhone: this.editStudentContactPhone || undefined,
        contactEmail: this.editStudentContactEmail || undefined,
      })
      .subscribe({
        next: () => {
          this.editingStudentId = null;
          this.reloadAll();
        },
        error: (err) => this.showError('updateStudent failed', err),
      });
  }

  /** Also removes the student's grades, absences, messages and parent
   * links — see StudentsController.DeleteStudent. The confirm() here is the
   * one place in this page a mistake can't be undone. */
  protected deleteStudent(student: Student): void {
    if (!confirm(`Opravdu smazat žáka ${student.firstName} ${student.lastName}? Smažou se i jeho známky, docházka a zprávy.`)) {
      return;
    }
    this.studentsApi.deleteStudent(student.id).subscribe({
      next: () => this.reloadAll(),
      error: (err) => this.showError('deleteStudent failed', err),
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
      phone: this.newUserPhone || undefined,
      email: this.newUserEmail || undefined,
    };
    this.usersApi.addUser(request).subscribe({
      next: () => {
        this.newUsername = '';
        this.newPassword = '';
        this.newUserPhone = '';
        this.newUserEmail = '';
        this.reloadAll();
      },
      error: (err) => this.showError('addUser failed', err),
    });
  }

  protected startEditUser(user: AppUser): void {
    this.editingUserId = user.id;
    this.editUsername = user.username;
    this.editUserRole = user.role;
    this.editUserPassword = '';
    this.editUserPhone = user.phone ?? '';
    this.editUserEmail = user.email ?? '';
  }

  protected cancelEditUser(): void {
    this.editingUserId = null;
  }

  protected saveEditUser(): void {
    if (this.editingUserId === null || !this.editUsername.trim()) {
      return;
    }
    const request: UpdateUserRequest = {
      username: this.editUsername,
      role: this.editUserRole,
      password: this.editUserPassword || undefined,
      phone: this.editUserPhone || undefined,
      email: this.editUserEmail || undefined,
    };
    this.usersApi.updateUser(this.editingUserId, request).subscribe({
      next: () => {
        this.editingUserId = null;
        this.reloadAll();
      },
      error: (err) => this.showError('updateUser failed', err),
    });
  }

  /** Backend rejects (400) deleting yourself or the school's last Admin. */
  protected deleteUser(user: AppUser): void {
    this.usersApi.deleteUser(user.id).subscribe({
      next: () => this.reloadAll(),
      error: (err) => this.showError('deleteUser failed', err),
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
        this.schoolStreet.set(school.street ?? '');
        this.schoolCity.set(school.city ?? '');
        this.schoolPostalCode.set(school.postalCode ?? '');
        this.schoolDirectorName.set(school.directorName ?? '');
        this.schoolEmail.set(school.email ?? '');
        this.schoolPhone.set(school.phone ?? '');
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

  protected saveSchoolDetails(): void {
    this.mySchoolApi
      .setDetails({
        street: this.schoolStreet() || null,
        city: this.schoolCity() || null,
        postalCode: this.schoolPostalCode() || null,
        directorName: this.schoolDirectorName() || null,
        email: this.schoolEmail() || null,
        phone: this.schoolPhone() || null,
      })
      .subscribe({
        next: () => this.loadMySchool(),
        error: (err) => this.showError('setSchoolDetails failed', err),
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
    this.error.set((err as { error?: string })?.error ?? 'Něco se nepovedlo.');
  }
}
