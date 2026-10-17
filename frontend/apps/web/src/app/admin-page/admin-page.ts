import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AutoComplete,
  AutoCompleteCompleteEvent,
} from '@openng/optimus-ui/autocomplete';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import { Password } from '@openng/optimus-ui/password';
import { Select } from '@openng/optimus-ui/select';
import { TableModule } from '@openng/optimus-ui/table';
import { Tag } from '@openng/optimus-ui/tag';
import {
  Tab,
  TabList,
  TabPanel,
  TabPanels,
  Tabs,
} from '@openng/optimus-ui/tabs';
import { forkJoin, Observable } from 'rxjs';
import {
  AppUser,
  AuthApiService,
  Classroom,
  ClassroomsApiService,
  Country,
  CreateUserRequest,
  Gender,
  MatrikaApiService,
  Municipality,
  MySchoolApiService,
  Parent as ParentUser,
  ParentsApiService,
  ParentStudentLink,
  ParentStudentsApiService,
  ReferenceApiService,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  UpdateUserRequest,
  UsersApiService,
} from '@sova/shared';

/** Admin: full access to every tab. ClassTeacher: a Teacher who owns at
 * least one class (SchoolClass.classTeacherId) gets a cut-down page — just
 * their own class's students and parent↔student links, nothing else (see
 * ROADMAP "Administrace — redesign"). Denied: anyone else who reaches this
 * route (a Teacher who isn't a class teacher of anything, or a non-Teacher
 * non-Admin navigating here directly). */
type AccessLevel = 'admin' | 'classTeacher' | 'denied' | 'loading';

const ROLE_OPTIONS = [
  { label: 'Admin', value: 'Admin' },
  { label: 'Teacher', value: 'Teacher' },
  { label: 'Parent', value: 'Parent' },
  { label: 'Student', value: 'Student' },
];

const GENDER_OPTIONS: { label: string; value: Gender }[] = [
  { label: 'Chlapec', value: 'Male' },
  { label: 'Dívka', value: 'Female' },
];

// Same fixed palette as events-page.ts's Event.Color picker (not a free
// picker) — distinct enough at a glance, muted enough to keep cell text
// readable on every swatch.
const SUBJECT_COLOR_PALETTE = [
  '#6366f1', // indigo
  '#ef4444', // red
  '#f97316', // orange
  '#eab308', // yellow
  '#22c55e', // green
  '#14b8a6', // teal
  '#3b82f6', // blue
  '#a855f7', // purple
  '#ec4899', // pink
  '#64748b', // slate
];

/** RAKO kvalifikátor státního občanství — only 8 stable rows, so a hardcoded
 * list (same pattern as GENDER_OPTIONS/ROLE_OPTIONS) rather than a DB table.
 * See Student.CitizenshipQualifier. */
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

@Component({
  selector: 'app-admin-page',
  imports: [
    AutoComplete,
    FormsModule,
    Button,
    Card,
    DatePicker,
    Dialog,
    InputNumber,
    InputText,
    Message,
    MultiSelect,
    NgTemplateOutlet,
    Password,
    PrimeTemplate,
    Select,
    Tab,
    TableModule,
    TabList,
    TabPanel,
    TabPanels,
    Tabs,
    Tag,
  ],
  templateUrl: './admin-page.html',
})
export class AdminPage {
  private readonly authApi = inject(AuthApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly parentsApi = inject(ParentsApiService);
  private readonly parentStudentsApi = inject(ParentStudentsApiService);
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly matrikaApi = inject(MatrikaApiService);
  private readonly referenceApi = inject(ReferenceApiService);

  protected readonly accessLevel = signal<AccessLevel>('loading');
  /** Only populated (and only relevant) for accessLevel 'classTeacher' —
   * which SchoolClass ids the current Teacher is the class teacher of.
   * Drives client-side filtering of `students`/`schoolClasses` below, since
   * the backend already scopes the write endpoints but GetStudents/
   * GetSchoolClasses stay unrestricted reads for any Teacher/Admin. */
  private ownClassIds: number[] = [];

  protected readonly students = signal<Student[]>([]);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly classrooms = signal<Classroom[]>([]);
  protected readonly classroomOptions = computed(() =>
    this.classrooms().map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly users = signal<AppUser[]>([]);
  protected readonly parents = signal<ParentUser[]>([]);
  protected readonly links = signal<ParentStudentLink[]>([]);
  protected readonly countries = signal<Country[]>([]);
  protected readonly municipalitySuggestions = signal<Municipality[]>([]);

  protected readonly activeTab = signal('classes');

  protected readonly newClassDialogVisible = signal(false);
  protected readonly newSubjectDialogVisible = signal(false);
  protected readonly newClassroomDialogVisible = signal(false);
  protected readonly newStudentDialogVisible = signal(false);
  protected readonly newUserDialogVisible = signal(false);
  protected readonly editParentChildrenDialogVisible = signal(false);

  protected readonly roleOptions = ROLE_OPTIONS;
  protected readonly genderOptions = GENDER_OPTIONS;
  protected readonly citizenshipQualifierOptions =
    CITIZENSHIP_QUALIFIER_OPTIONS;
  protected readonly subjectColorPalette = SUBJECT_COLOR_PALETTE;

  /** p-select's optionLabel only takes a property path, not a function. */
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      id: s.id,
      label: `${s.firstName} ${s.lastName}`,
    })),
  );

  /** Třídní učitel candidates — Teacher role only. */
  protected readonly teacherOptions = computed(() =>
    this.users().filter((u) => u.role === 'Teacher'),
  );

  /** "Rodiče" overview list — Admin sees every Parent-role account; a class
   * teacher only sees ones already linked to one of their own-class
   * students (their `links()` is already server-scoped — see
   * ParentStudentsController.GetLinks), so an unrelated parent elsewhere in
   * the school isn't shown. The multi-select picker *inside* the dialogs
   * below (both directions) always uses the full `parents()` — scoping
   * there is enforced by the backend per student, not by hiding the parent
   * account from the picker. */
  protected readonly visibleParents = computed(() =>
    this.accessLevel() === 'admin'
      ? this.parents()
      : this.parents().filter((p) =>
          this.links().some((l) => l.userId === p.id),
        ),
  );

  /** Students pickable for a new/edited Student-role account — excludes
   * students that already have one, except the student currently linked to
   * the account being edited (so re-saving it without changing the link
   * doesn't disappear its own option from the list). A plain method, not a
   * computed signal, because it depends on `editingUserId` — a plain field,
   * not a signal (same reasoning as `filteredAbsences()` elsewhere): a
   * computed here would go stale the moment editingUserId changes without
   * `users()`/`studentOptions()` also changing. */
  protected availableStudentOptions(): { id: number; label: string }[] {
    const linkedElsewhere = new Set(
      this.users()
        .filter(
          (u) =>
            u.role === 'Student' &&
            u.studentId !== null &&
            u.id !== this.editingUserId,
        )
        .map((u) => u.studentId),
    );
    return this.studentOptions().filter((s) => !linkedElsewhere.has(s.id));
  }

  // The "new…" fields below double as the dialog's form state for BOTH
  // adding and editing — one dialog, one set of fields, same pattern as
  // Rozvrh's "Nová hodina"/"Upravit hodinu" dialog. Which mode it's in is
  // just "is editingXId null" (see openAddXDialog/startEditX/saveX below).
  protected newSchoolClassName = '';
  protected newSchoolClassRocnik: number | null = null;
  protected newSchoolClassTeacherId: number | null = null;
  protected newSchoolClassTeachingLanguage = '';
  protected newSubjectName = '';
  protected newSubjectAbbreviation = '';
  protected newSubjectColor: string | null = null;
  protected newSubjectDefaultClassroomId: number | null = null;
  protected newClassroomName = '';
  protected newClassroomType = '';
  protected newClassroomCapacity: number | null = null;

  protected newStudentFirstName = '';
  protected newStudentLastName = '';
  protected newStudentSchoolClassId: number | null = null;
  protected newStudentRodneCislo = '';
  protected newStudentDateOfBirth: Date | null = null;
  protected newStudentGender: Gender | null = null;
  protected newStudentCitizenshipQualifier: string | null = null;
  protected newStudentCountryCode: string | null = null;
  protected newStudentStreet = '';
  protected newStudentMunicipality: Municipality | null = null;
  protected newStudentPostalCode = '';
  protected newStudentCatchmentArea = false;
  protected newStudentEnrollmentDate: Date | null = null;
  protected newStudentContactPhone = '';
  protected newStudentContactEmail = '';

  protected newUsername = '';
  protected newPassword = '';
  protected newUserRole = 'Parent';
  protected newUserPhone = '';
  protected newUserEmail = '';
  protected newUserStudentId: number | null = null;

  protected filterStudentName = '';
  protected filterStudentSchoolClassId: number | null = null;
  protected filterStudentGender: Gender | null = null;
  protected filterUserRole: string | null = null;

  // null = the dialog is in "add" mode; a row id = it's editing that row.
  protected editingSchoolClassId: number | null = null;
  protected editingSubjectId: number | null = null;
  protected editingClassroomId: number | null = null;
  protected editingStudentId: number | null = null;
  protected editingUserId: number | null = null;

  /** Multi-select state for the Student dialog's "Rodiče" field — only
   * meaningful (and only shown) when editing an existing student
   * (editingStudentId !== null); a brand new student has no id yet to
   * attach ParentStudent links to. */
  protected editingStudentParentIds: number[] = [];

  /** "Rodiče" section's own edit dialog — null editingParentId means the
   * dialog isn't open (unlike the other entities above, there's no "add a
   * parent" mode here: a Parent-role account is created from the
   * Uživatelé tab like any other user, this dialog only ever edits an
   * existing one's children). */
  protected editingParentId: number | null = null;
  protected editingParentChildIds: number[] = [];

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
    if (this.authApi.isAdmin()) {
      this.accessLevel.set('admin');
      this.reload();
      this.loadMySchool();
      this.referenceApi.getCountries().subscribe({
        next: (countries) => this.countries.set(countries),
        error: (err) => console.error('getCountries failed', err),
      });
    } else if (this.authApi.isTeacher()) {
      this.resolveClassTeacherAccess();
    } else {
      this.accessLevel.set('denied');
    }
  }

  /** A Teacher only gets into this page at all as the class teacher
   * (`SchoolClass.classTeacherId`) of at least one class — resolved
   * client-side from the already-unrestricted `GET /api/school-classes`
   * read (same call Admin's full view makes), not a dedicated "am I a
   * class teacher" endpoint. A Teacher who owns no class is denied, same
   * as any other non-Admin landing here directly. */
  private resolveClassTeacherAccess(): void {
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (classes) => {
        const myId = this.authApi.getUserId();
        this.ownClassIds = classes
          .filter((c) => c.classTeacherId === myId)
          .map((c) => c.id);
        if (this.ownClassIds.length === 0) {
          this.accessLevel.set('denied');
          return;
        }
        this.accessLevel.set('classTeacher');
        this.schoolClasses.set(
          classes.filter((c) => this.ownClassIds.includes(c.id)),
        );
        this.reloadClassTeacherData();
      },
      error: (err) => {
        console.error('getSchoolClasses failed', err);
        this.accessLevel.set('denied');
      },
    });
  }

  /** Dispatches to whichever reload matches the resolved access level —
   * every mutation handler below (addStudent, addLink, ...) calls this
   * rather than `reloadAll`/`reloadClassTeacherData` directly, so the same
   * handler works correctly regardless of which view is active. */
  private reload(): void {
    if (this.accessLevel() === 'classTeacher') {
      this.reloadClassTeacherData();
    } else {
      this.reloadAll();
    }
  }

  /** Local (not UTC) y-m-d — `toISOString().slice(0, 10)` converts through
   * UTC first, which rolls local midnight back to the previous day in any
   * timezone ahead of UTC (e.g. CET/CEST). Same helper as absences-page.ts. */
  private toDateOnlyString(date: Date): string {
    const year = date.getFullYear();
    const month = `${date.getMonth() + 1}`.padStart(2, '0');
    const day = `${date.getDate()}`.padStart(2, '0');
    return `${year}-${month}-${day}`;
  }

  private reloadClassTeacherData(): void {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        const scoped = students.filter((s) =>
          this.ownClassIds.includes(s.schoolClassId),
        );
        this.students.set(scoped);
        this.newStudentSchoolClassId ??= this.ownClassIds[0] ?? null;
      },
      error: (err) => console.error('getStudents failed', err),
    });
    // Already scoped server-side for a Teacher caller — see
    // ParentStudentsController.GetLinks.
    this.parentStudentsApi.getLinks().subscribe({
      next: (links) => this.links.set(links),
      error: (err) => console.error('getLinks failed', err),
    });
    this.parentsApi.getParents().subscribe({
      next: (parents) => this.parents.set(parents),
      error: (err) => console.error('getParents failed', err),
    });
  }

  /** p-autoComplete's completeMethod — backend ignores queries under 2 chars. */
  protected searchMunicipalities(event: AutoCompleteCompleteEvent): void {
    this.referenceApi.searchMunicipalities(event.query).subscribe({
      next: (results) => this.municipalitySuggestions.set(results),
      error: (err) => console.error('searchMunicipalities failed', err),
    });
  }

  protected classTeacherName(classTeacherId: number | null): string {
    if (classTeacherId === null) {
      return '—';
    }
    return this.users().find((u) => u.id === classTeacherId)?.username ?? '—';
  }

  protected studentName(studentId: number | null): string {
    if (studentId === null) {
      return '—';
    }
    const student = this.students().find((s) => s.id === studentId);
    return student ? `${student.firstName} ${student.lastName}` : '—';
  }

  /** All parents assigned to a student, joined on one line — no limit on
   * how many (grandparents, step-parents...), so the caller (the Studenti
   * table) truncates with an ellipsis + title tooltip rather than this
   * method itself picking a cutoff. */
  protected studentParentNames(studentId: number): string {
    const names = this.links()
      .filter((l) => l.studentId === studentId)
      .map((l) => l.username);
    return names.length > 0 ? names.join(', ') : '—';
  }

  /** Mirror of studentParentNames() for the Rodiče table — all children
   * assigned to a parent, joined on one line. */
  protected parentChildNames(userId: number): string {
    const names = this.links()
      .filter((l) => l.userId === userId)
      .map((l) => l.studentName);
    return names.length > 0 ? names.join(', ') : '—';
  }

  protected filteredStudents(): Student[] {
    const name = this.filterStudentName.trim().toLowerCase();
    return this.students().filter(
      (s) =>
        (!name ||
          `${s.firstName} ${s.lastName}`.toLowerCase().includes(name)) &&
        (this.filterStudentSchoolClassId === null ||
          s.schoolClassId === this.filterStudentSchoolClassId) &&
        (this.filterStudentGender === null ||
          s.gender === this.filterStudentGender),
    );
  }

  protected filteredUsers(): AppUser[] {
    return this.users().filter(
      (u) => this.filterUserRole === null || u.role === this.filterUserRole,
    );
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
    this.classroomsApi.getClassrooms().subscribe({
      next: (classrooms) => this.classrooms.set(classrooms),
      error: (err) => console.error('getClassrooms failed', err),
    });
    this.usersApi.getUsers().subscribe({
      next: (users) => this.users.set(users),
      error: (err) => console.error('getUsers failed', err),
    });
    this.parentStudentsApi.getLinks().subscribe({
      next: (links) => this.links.set(links),
      error: (err) => console.error('getLinks failed', err),
    });
    this.parentsApi.getParents().subscribe({
      next: (parents) => this.parents.set(parents),
      error: (err) => console.error('getParents failed', err),
    });
  }

  protected openAddClassDialog(): void {
    this.editingSchoolClassId = null;
    this.newSchoolClassName = '';
    this.newSchoolClassRocnik = null;
    this.newSchoolClassTeacherId = null;
    this.newSchoolClassTeachingLanguage = '';
    this.newClassDialogVisible.set(true);
  }

  protected startEditSchoolClass(schoolClass: SchoolClass): void {
    this.editingSchoolClassId = schoolClass.id;
    this.newSchoolClassName = schoolClass.name;
    this.newSchoolClassRocnik = schoolClass.rocnik;
    this.newSchoolClassTeacherId = schoolClass.classTeacherId;
    this.newSchoolClassTeachingLanguage = schoolClass.teachingLanguage ?? '';
    this.newClassDialogVisible.set(true);
  }

  protected saveSchoolClass(): void {
    if (!this.newSchoolClassName.trim()) {
      return;
    }
    const request = {
      name: this.newSchoolClassName,
      rocnik: this.newSchoolClassRocnik ?? undefined,
      classTeacherId: this.newSchoolClassTeacherId ?? undefined,
      teachingLanguage: this.newSchoolClassTeachingLanguage || undefined,
    };
    const result =
      this.editingSchoolClassId === null
        ? this.schoolClassesApi.addSchoolClass(request)
        : this.schoolClassesApi.updateSchoolClass(
            this.editingSchoolClassId,
            request,
          );
    result.subscribe({
      next: () => {
        this.newClassDialogVisible.set(false);
        this.editingSchoolClassId = null;
        this.reload();
      },
      error: (err) => this.showError('saveSchoolClass failed', err),
    });
  }

  protected deleteSchoolClass(schoolClass: SchoolClass): void {
    this.schoolClassesApi.deleteSchoolClass(schoolClass.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.showError('deleteSchoolClass failed', err),
    });
  }

  protected openAddSubjectDialog(): void {
    this.editingSubjectId = null;
    this.newSubjectName = '';
    this.newSubjectAbbreviation = '';
    this.newSubjectColor = null;
    this.newSubjectDefaultClassroomId = null;
    this.newSubjectDialogVisible.set(true);
  }

  protected startEditSubject(subject: Subject): void {
    this.editingSubjectId = subject.id;
    this.newSubjectName = subject.name;
    this.newSubjectAbbreviation = subject.abbreviation ?? '';
    this.newSubjectColor = subject.color;
    this.newSubjectDefaultClassroomId = subject.defaultClassroomId;
    this.newSubjectDialogVisible.set(true);
  }

  protected saveSubject(): void {
    if (!this.newSubjectName.trim()) {
      return;
    }
    const request = {
      name: this.newSubjectName,
      abbreviation: this.newSubjectAbbreviation || undefined,
      color: this.newSubjectColor || undefined,
      defaultClassroomId: this.newSubjectDefaultClassroomId ?? undefined,
    };
    const result =
      this.editingSubjectId === null
        ? this.subjectsApi.addSubject(request)
        : this.subjectsApi.updateSubject(this.editingSubjectId, request);
    result.subscribe({
      next: () => {
        this.newSubjectDialogVisible.set(false);
        this.editingSubjectId = null;
        this.reload();
      },
      error: (err) => this.showError('saveSubject failed', err),
    });
  }

  protected deleteSubject(subject: Subject): void {
    this.subjectsApi.deleteSubject(subject.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.showError('deleteSubject failed', err),
    });
  }

  protected classroomName(classroomId: number | null): string {
    if (classroomId === null) {
      return '—';
    }
    return this.classrooms().find((c) => c.id === classroomId)?.name ?? '—';
  }

  protected openAddClassroomDialog(): void {
    this.editingClassroomId = null;
    this.newClassroomName = '';
    this.newClassroomType = '';
    this.newClassroomCapacity = null;
    this.newClassroomDialogVisible.set(true);
  }

  protected startEditClassroom(classroom: Classroom): void {
    this.editingClassroomId = classroom.id;
    this.newClassroomName = classroom.name;
    this.newClassroomType = classroom.type ?? '';
    this.newClassroomCapacity = classroom.capacity;
    this.newClassroomDialogVisible.set(true);
  }

  protected saveClassroom(): void {
    if (!this.newClassroomName.trim()) {
      return;
    }
    const request = {
      name: this.newClassroomName,
      type: this.newClassroomType || undefined,
      capacity: this.newClassroomCapacity ?? undefined,
    };
    const result =
      this.editingClassroomId === null
        ? this.classroomsApi.addClassroom(request)
        : this.classroomsApi.updateClassroom(this.editingClassroomId, request);
    result.subscribe({
      next: () => {
        this.newClassroomDialogVisible.set(false);
        this.editingClassroomId = null;
        this.reload();
      },
      error: (err) => this.showError('saveClassroom failed', err),
    });
  }

  protected deleteClassroom(classroom: Classroom): void {
    this.classroomsApi.deleteClassroom(classroom.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.showError('deleteClassroom failed', err),
    });
  }

  protected openAddStudentDialog(): void {
    this.editingStudentId = null;
    this.newStudentFirstName = '';
    this.newStudentLastName = '';
    this.newStudentSchoolClassId = this.schoolClasses()[0]?.id ?? null;
    this.newStudentRodneCislo = '';
    this.newStudentDateOfBirth = null;
    this.newStudentGender = null;
    this.newStudentCitizenshipQualifier = null;
    this.newStudentCountryCode = null;
    this.newStudentStreet = '';
    this.newStudentMunicipality = null;
    this.newStudentPostalCode = '';
    this.newStudentCatchmentArea = false;
    this.newStudentEnrollmentDate = null;
    this.newStudentContactPhone = '';
    this.newStudentContactEmail = '';
    this.editingStudentParentIds = [];
    this.newStudentDialogVisible.set(true);
  }

  protected startEditStudent(student: Student): void {
    this.editingStudentId = student.id;
    this.editingStudentParentIds = this.links()
      .filter((l) => l.studentId === student.id)
      .map((l) => l.userId);
    this.newStudentFirstName = student.firstName;
    this.newStudentLastName = student.lastName;
    this.newStudentSchoolClassId = student.schoolClassId;
    this.newStudentRodneCislo = student.rodneCislo ?? '';
    this.newStudentDateOfBirth = student.dateOfBirth
      ? new Date(student.dateOfBirth)
      : null;
    this.newStudentGender = student.gender;
    this.newStudentCitizenshipQualifier = student.citizenshipQualifier;
    this.newStudentCountryCode = student.countryCode;
    this.newStudentStreet = student.street ?? '';
    this.newStudentMunicipality = student.municipalityCode
      ? {
          code: student.municipalityCode,
          name: student.municipalityName ?? student.municipalityCode,
          districtCode: '',
        }
      : null;
    this.newStudentPostalCode = student.postalCode ?? '';
    this.newStudentCatchmentArea = student.catchmentArea ?? false;
    this.newStudentEnrollmentDate = student.enrollmentDate
      ? new Date(student.enrollmentDate)
      : null;
    this.newStudentContactPhone = student.contactPhone ?? '';
    this.newStudentContactEmail = student.contactEmail ?? '';
    this.newStudentDialogVisible.set(true);
  }

  protected saveStudent(): void {
    if (
      !this.newStudentFirstName.trim() ||
      !this.newStudentLastName.trim() ||
      !this.newStudentSchoolClassId
    ) {
      return;
    }
    const request = {
      firstName: this.newStudentFirstName,
      lastName: this.newStudentLastName,
      schoolClassId: this.newStudentSchoolClassId,
      rodneCislo: this.newStudentRodneCislo || undefined,
      dateOfBirth: this.newStudentDateOfBirth
        ? this.toDateOnlyString(this.newStudentDateOfBirth)
        : undefined,
      gender: this.newStudentGender ?? undefined,
      citizenshipQualifier: this.newStudentCitizenshipQualifier || undefined,
      countryCode: this.newStudentCountryCode || undefined,
      street: this.newStudentStreet || undefined,
      municipalityCode: this.newStudentMunicipality?.code || undefined,
      postalCode: this.newStudentPostalCode || undefined,
      catchmentArea: this.newStudentCatchmentArea || undefined,
      enrollmentDate: this.newStudentEnrollmentDate
        ? this.toDateOnlyString(this.newStudentEnrollmentDate)
        : undefined,
      contactPhone: this.newStudentContactPhone || undefined,
      contactEmail: this.newStudentContactEmail || undefined,
    };
    const editingId = this.editingStudentId;
    const result =
      editingId === null
        ? this.studentsApi.addStudent(request)
        : this.studentsApi.updateStudent(editingId, request);
    result.subscribe({
      next: () => {
        if (editingId === null) {
          // A brand new student has no id yet to attach ParentStudent links
          // to — the "Rodiče" field is hidden for "add" (see the dialog
          // template), so there's never anything to diff here.
          this.newStudentDialogVisible.set(false);
          this.reload();
          return;
        }
        const currentParentIds = this.links()
          .filter((l) => l.studentId === editingId)
          .map((l) => l.userId);
        const toAdd = this.editingStudentParentIds.filter(
          (id) => !currentParentIds.includes(id),
        );
        const toRemove = currentParentIds.filter(
          (id) => !this.editingStudentParentIds.includes(id),
        );
        this.applyLinkChanges(
          toAdd.map((userId) =>
            this.parentStudentsApi.addLink({ userId, studentId: editingId }),
          ),
          toRemove.map((userId) =>
            this.parentStudentsApi.removeLink(userId, editingId),
          ),
          () => {
            this.newStudentDialogVisible.set(false);
            this.editingStudentId = null;
            this.reload();
          },
        );
      },
      error: (err) => this.showError('saveStudent failed', err),
    });
  }

  /** Also removes the student's grades, absences, messages and parent
   * links — see StudentsController.DeleteStudent. The confirm() here is the
   * one place in this page a mistake can't be undone. */
  protected deleteStudent(student: Student): void {
    if (
      !confirm(
        `Opravdu smazat žáka ${student.firstName} ${student.lastName}? Smažou se i jeho známky, docházka a zprávy.`,
      )
    ) {
      return;
    }
    this.studentsApi.deleteStudent(student.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.showError('deleteStudent failed', err),
    });
  }

  protected openAddUserDialog(): void {
    this.editingUserId = null;
    this.newUsername = '';
    this.newPassword = '';
    this.newUserRole = 'Parent';
    this.newUserPhone = '';
    this.newUserEmail = '';
    this.newUserStudentId = null;
    this.newUserDialogVisible.set(true);
  }

  protected startEditUser(user: AppUser): void {
    this.editingUserId = user.id;
    this.newUsername = user.username;
    this.newUserRole = user.role;
    this.newPassword = ''; // left blank = keep the current password
    this.newUserPhone = user.phone ?? '';
    this.newUserEmail = user.email ?? '';
    this.newUserStudentId = user.studentId;
    this.newUserDialogVisible.set(true);
  }

  protected saveUser(): void {
    if (!this.newUsername.trim()) {
      return;
    }
    const studentId =
      this.newUserRole === 'Student'
        ? (this.newUserStudentId ?? undefined)
        : undefined;

    if (this.editingUserId === null) {
      if (!this.newPassword.trim()) {
        return;
      }
      const request: CreateUserRequest = {
        username: this.newUsername,
        password: this.newPassword,
        role: this.newUserRole,
        phone: this.newUserPhone || undefined,
        email: this.newUserEmail || undefined,
        studentId,
      };
      this.usersApi.addUser(request).subscribe({
        next: () => {
          this.newUserDialogVisible.set(false);
          this.reload();
        },
        error: (err) => this.showError('addUser failed', err),
      });
      return;
    }

    const request: UpdateUserRequest = {
      username: this.newUsername,
      role: this.newUserRole,
      password: this.newPassword || undefined,
      phone: this.newUserPhone || undefined,
      email: this.newUserEmail || undefined,
      studentId,
    };
    this.usersApi.updateUser(this.editingUserId, request).subscribe({
      next: () => {
        this.newUserDialogVisible.set(false);
        this.editingUserId = null;
        this.reload();
      },
      error: (err) => this.showError('updateUser failed', err),
    });
  }

  /** Backend rejects (400) deleting yourself or the school's last Admin. */
  protected deleteUser(user: AppUser): void {
    this.usersApi.deleteUser(user.id).subscribe({
      next: () => this.reload(),
      error: (err) => this.showError('deleteUser failed', err),
    });
  }

  protected openEditParentChildrenDialog(parent: AppUser): void {
    this.editingParentId = parent.id;
    this.editingParentChildIds = this.links()
      .filter((l) => l.userId === parent.id)
      .map((l) => l.studentId);
    this.editParentChildrenDialogVisible.set(true);
  }

  protected saveParentChildren(): void {
    if (this.editingParentId === null) {
      return;
    }
    const parentId = this.editingParentId;
    const currentStudentIds = this.links()
      .filter((l) => l.userId === parentId)
      .map((l) => l.studentId);
    const toAdd = this.editingParentChildIds.filter(
      (id) => !currentStudentIds.includes(id),
    );
    const toRemove = currentStudentIds.filter(
      (id) => !this.editingParentChildIds.includes(id),
    );
    this.applyLinkChanges(
      [
        ...toAdd.map((studentId) =>
          this.parentStudentsApi.addLink({ userId: parentId, studentId }),
        ),
      ],
      [
        ...toRemove.map((studentId) =>
          this.parentStudentsApi.removeLink(parentId, studentId),
        ),
      ],
      () => {
        this.editParentChildrenDialogVisible.set(false);
        this.editingParentId = null;
        this.reload();
      },
    );
  }

  /** Runs a batch of ParentStudent add/remove calls (built by the caller,
   * since "add"/"remove" need different arguments and there's no single
   * endpoint for "set the full set at once") and reloads once they've all
   * settled — shared by saveParentChildren() above and saveStudent()'s own
   * "Rodiče" diff below, so the fan-out/reload plumbing isn't duplicated. */
  private applyLinkChanges(
    additions: Observable<unknown>[],
    removals: Observable<unknown>[],
    onDone: () => void,
  ): void {
    const requests = [...additions, ...removals];
    if (requests.length === 0) {
      onDone();
      return;
    }
    forkJoin(requests).subscribe({
      next: () => onDone(),
      error: (err) => this.showError('saving parent↔student links failed', err),
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
      .setMatrikaIds({
        izo: this.matrikaIzo() || null,
        redIzo: this.matrikaRedIzo() || null,
      })
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
          const fileName =
            this.extractFileName(response.headers.get('Content-Disposition')) ??
            'matrika-export.xml';
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
        const parsed = JSON.parse(text) as {
          errors?: string[] | Record<string, string[]>;
        };
        this.matrikaExportErrors.set(
          this.flattenErrors(parsed.errors) ?? ['Export se nepovedl.'],
        );
      })
      .catch(() => this.matrikaExportErrors.set(['Export se nepovedl.']));
  }

  /** Our own 400s send `{ errors: string[] }`; ASP.NET's automatic model
   * validation (e.g. a required query param left empty) sends
   * `{ errors: { field: string[] } }` instead — normalize both to a flat list. */
  private flattenErrors(
    errors: string[] | Record<string, string[]> | undefined,
  ): string[] | null {
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
