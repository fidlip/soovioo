import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
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

@Component({
  selector: 'app-admin-page',
  imports: [FormsModule, Button, Card, InputText, Message, Password, PrimeTemplate, Select, TableModule, Tag],
  templateUrl: './admin-page.html',
})
export class AdminPage {
  private readonly authApi = inject(AuthApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly parentStudentsApi = inject(ParentStudentsApiService);

  protected readonly students = signal<Student[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly users = signal<AppUser[]>([]);
  protected readonly links = signal<ParentStudentLink[]>([]);

  protected readonly roleOptions = ROLE_OPTIONS;

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({ id: s.id, label: `${s.firstName} ${s.lastName}` })),
  );

  protected newSchoolClassName = '';
  protected newSubjectName = '';

  protected newStudentFirstName = '';
  protected newStudentLastName = '';
  protected newStudentSchoolClassId: number | null = null;

  protected newUsername = '';
  protected newPassword = '';
  protected newUserRole = 'Parent';

  protected newLinkUserId: number | null = null;
  protected newLinkStudentId: number | null = null;

  protected error: string | null = null;

  constructor() {
    this.reloadAll();
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
    this.schoolClassesApi.addSchoolClass({ name: this.newSchoolClassName }).subscribe({
      next: () => {
        this.newSchoolClassName = '';
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
      })
      .subscribe({
        next: () => {
          this.newStudentFirstName = '';
          this.newStudentLastName = '';
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

  private showError(context: string, err: unknown): void {
    console.error(context, err);
    this.error = (err as { error?: string })?.error ?? 'Něco se nepovedlo.';
  }
}
