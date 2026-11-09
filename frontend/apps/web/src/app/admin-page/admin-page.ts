import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Params, Router } from '@angular/router';
import { PrimeTemplate } from '@openng/optimus-ui/api';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputNumber } from '@openng/optimus-ui/inputnumber';
import { InputText } from '@openng/optimus-ui/inputtext';
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
import { forkJoin, Observable, switchMap } from 'rxjs';
import {
  AppUser,
  AuthApiService,
  CLASSROOM_TYPE_LABELS,
  Classroom,
  ClassroomType,
  ClassroomsApiService,
  Country,
  CreateUserRequest,
  DateFormatPipe,
  Gender,
  MatrikaApiService,
  MySchoolApiService,
  Parent as ParentUser,
  ParentStudentLink,
  ParentStudentsApiService,
  ParentsApiService,
  PeopleApiService,
  Person,
  PhotosApiService,
  ReferenceApiService,
  RoleRef,
  RolesApiService,
  SchoolClass,
  RvpProgramOption,
  SchoolClassesApiService,
  TeachingApiService,
  SchoolType,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
  UpdateUserRequest,
  UsersApiService,
  formatIban,
  localizeStudentTerm,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';
import { NicknameApproval } from '../nickname-approval/nickname-approval';
import { foldForSearch, matchesContact } from '../text-search';

/** The sections each group of Administrace shows itself; Výuka and Archiv
 * have their own (TeachingTab, Departures). */
const GROUP_SECTIONS: Record<string, string[]> = {
  school: [
    'details',
    'registry',
    'classes',
    'subjects',
    'classrooms',
    'people',
  ],
  app: ['users', 'roles', 'visibility', 'notifications'],
  class: ['students', 'parents'],
};

/** Admin: full access to every tab. ClassTeacher: a Teacher who owns at
 * least one class (SchoolClass.classTeacherId) gets a cut-down page — just
 * their own class's students and parent↔student links, nothing else (see
 * ROADMAP "Administrace — redesign"). Denied: anyone else who reaches this
 * route (a Teacher who isn't a class teacher of anything, or a non-Teacher
 * non-Admin navigating here directly). */
type AccessLevel = 'admin' | 'classTeacher' | 'teaching' | 'denied' | 'loading';

/** The person profile a role needs — the built-in Teacher a teacher's, a
 * parent's or pupil's role theirs, other staff roles none. */
function profileOf(role: RoleRef): PersonProfile | null {
  return role.builtIn === 'Teacher'
    ? 'teacher'
    : role.kind === 'Parent'
      ? 'parent'
      : role.kind === 'Student'
        ? 'student'
        : null;
}
const PROFILE_NAMES: Record<PersonProfile, string> = {
  teacher: 'učitele',
  parent: 'rodiče',
  student: 'žáka',
};

// Fixed pastel palette (Tailwind 200 shades, not a free picker) — soft
// enough not to dominate the timetable and keep dark cell text readable.
const SUBJECT_COLOR_PALETTE = [
  '#c7d2fe', // indigo
  '#fecaca', // red
  '#fed7aa', // orange
  '#fef08a', // yellow
  '#bbf7d0', // green
  '#99f6e4', // teal
  '#bfdbfe', // blue
  '#e9d5ff', // purple
  '#fbcfe8', // pink
  '#e2e8f0', // slate
];

import { InfoHint } from '../info-hint/info-hint';
import { ReportCardSettings } from './report-card-settings';
import { ConfirmationService } from '@openng/optimus-ui/api';
import { NotifyService } from '../notify.service';
import { DemoEmailPreviewService } from '../demo/demo-email-preview';
import { ClearFilters, FloatField, FORM_FIELDS } from '../forms';
import { SchoolNotifications } from '../school-notifications/school-notifications';
import { VisibilitySettings } from '../visibility-settings/visibility-settings';
import { Departures } from '../departures/departures';
import { TeachingTab } from '../teaching/teaching-tab';
import { canSeeTeachingArea } from '../teaching/teaching-area-access';
import { PeopleTab } from '../people/people-tab';
import { RolesTab } from '../roles/roles-tab';
import { PersonCard } from '../people/person-card';
import {
  PersonDialog,
  PersonDialogTarget,
  PersonProfile,
} from '../people/person-dialog';
import {
  GENDER_OPTIONS,
  StudentFields,
  StudentForm,
  emptyStudentForm,
  matrikaOf,
  studentFormOf,
} from '../people/student-fields';
import {
  StaffLeaveDialog,
  StaffLeaveTarget,
} from '../departures/staff-leave-dialog';
import { Avatar } from '../avatar/avatar';
import {
  StudentPhotoDialog,
  UserPhotoDialog,
} from '../photo-dialogs/photo-dialogs';
import { Tooltip } from '@openng/optimus-ui/tooltip';
import { Loading } from '../loading/loading';
@Component({
  selector: 'app-admin-page',
  imports: [
    NicknameApproval,
    ClearFilters,
    FloatField,
    DateFormatPipe,
    Loading,
    Checkbox,
    SchoolNotifications,
    VisibilitySettings,
    Departures,
    PeopleTab,
    TeachingTab,
    RolesTab,
    PersonCard,
    PersonDialog,
    StudentFields,
    StaffLeaveDialog,
    Avatar,
    StudentPhotoDialog,
    UserPhotoDialog,
    ...FORM_FIELDS,
    InfoHint,
    ReportCardSettings,
    TermPipe,
    AppIcon,
    FormsModule,
    Button,
    Card,
    Dialog,
    InputNumber,
    InputText,
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
    Tooltip,
  ],
  templateUrl: './admin-page.html',
})
export class AdminPage {
  protected readonly notify = inject(NotifyService);
  private readonly demoEmails = inject(DemoEmailPreviewService);
  private readonly confirmation = inject(ConfirmationService);
  private readonly authApi = inject(AuthApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly teachingApi = inject(TeachingApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly rolesApi = inject(RolesApiService);
  private readonly parentsApi = inject(ParentsApiService);
  private readonly parentStudentsApi = inject(ParentStudentsApiService);
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly peopleApi = inject(PeopleApiService);
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
  protected readonly studentsLoaded = signal(false);
  protected readonly schoolClasses = signal<SchoolClass[]>([]);
  protected readonly subjects = signal<Subject[]>([]);
  protected readonly classroomTypeOptions = Object.entries(
    CLASSROOM_TYPE_LABELS,
  ).map(([value, label]) => ({ value: value as ClassroomType, label }));
  protected readonly classrooms = signal<Classroom[]>([]);
  protected readonly classroomOptions = computed(() =>
    this.classrooms().map((c) => ({ id: c.id, label: c.name })),
  );
  protected readonly rvpPrograms = signal<RvpProgramOption[]>([]);
  protected readonly homeroomOptions = computed(() =>
    this.classrooms()
      .filter((c) => c.type === 'Homeroom')
      .map((c) => ({ id: c.id, label: c.name })),
  );
  /** A subject's classroom in its dialog: "room:<id>" or "type:<type>". */
  protected readonly subjectClassroomChoices = computed(() => [
    ...this.classroomTypeOptions.map((t) => ({
      value: `type:${t.value}`,
      label: `Jakákoli: ${t.label}`,
    })),
    ...this.classrooms().map((c) => ({ value: `room:${c.id}`, label: c.name })),
  ]);
  protected readonly newSubjectClassrooms = signal<(string | null)[]>([]);
  protected readonly users = signal<AppUser[]>([]);
  protected readonly usersLoaded = signal(false);
  /** Photo dialogs (see photo-dialogs.ts) — open while set. */
  protected readonly photoStudentId = signal<number | null>(null);
  protected readonly photoUser = signal<AppUser | null>(null);
  protected readonly parents = signal<ParentUser[]>([]);
  protected readonly parentsLoaded = signal(false);
  protected readonly links = signal<ParentStudentLink[]>([]);
  protected readonly countries = signal<Country[]>([]);

  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly query = toSignal(this.route.queryParamMap, {
    initialValue: this.route.snapshot.queryParamMap,
  });

  protected readonly groups = computed(() => {
    const teaching = { value: 'teaching', label: 'Výuka' };
    return this.accessLevel() === 'admin'
      ? [
          { value: 'school', label: 'Škola' },
          teaching,
          { value: 'app', label: 'Aplikace' },
          { value: 'archive', label: 'Archiv' },
        ]
      : [
          { value: 'class', label: 'Moje třída' },
          ...(canSeeTeachingArea(this.authApi) ? [teaching] : []),
        ];
  });
  /** The open group and section are in the address: a reload or a link
   * keeps them. */
  protected readonly group = computed(() => {
    const groups = this.groups();
    const wanted = this.query().get('group');
    return groups.find((g) => g.value === wanted)?.value ?? groups[0].value;
  });
  protected readonly section = computed(() => this.query().get('section'));
  protected readonly groupSection = computed(() => {
    const sections = GROUP_SECTIONS[this.group()] ?? [];
    const wanted = this.section();
    return wanted !== null && sections.includes(wanted) ? wanted : sections[0];
  });

  protected openGroup(group: string | number | undefined): void {
    this.teachingFocus.set(null);
    this.navigate({ group });
  }

  protected openSection(section: string | number | undefined | null): void {
    this.teachingFocus.set(null);
    this.navigate({ group: this.group(), section });
  }

  private navigate(queryParams: Params): void {
    this.router.navigate([], {
      relativeTo: this.route,
      queryParams,
      replaceUrl: true,
    });
  }

  protected readonly newClassDialogVisible = signal(false);
  protected readonly newSubjectDialogVisible = signal(false);
  protected readonly newClassroomDialogVisible = signal(false);
  protected readonly newStudentDialogVisible = signal(false);
  protected readonly newUserDialogVisible = signal(false);
  protected readonly editParentChildrenDialogVisible = signal(false);

  /** The school's roles (Administrace → Role). */
  protected readonly schoolRoles = signal<RoleRef[]>([]);
  protected readonly roleOptions = computed(() =>
    this.schoolRoles().map((role) => ({
      value: role.id,
      label: role.name,
      disabled: false,
    })),
  );
  /** For the admin's own account: dropping Admin would lock them out of
   * this page (the backend rejects it too). */
  protected readonly ownRoleOptions = computed(() =>
    this.roleOptions().map((option) => ({
      ...option,
      disabled: option.value === this.adminRoleId(),
    })),
  );
  protected readonly myUserId = this.authApi.getUserId();
  protected readonly genderOptions = GENDER_OPTIONS;
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
    this.users().filter((u) => u.roles.some((r) => r.builtIn === 'Teacher')),
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

  private readonly peopleTab = viewChild<PeopleTab>('peopleTab');

  /** Everyone at the school — the account dialog's choice of person. */
  protected readonly people = signal<Person[]>([]);
  /** The person the account being added/edited is. */
  protected readonly userPerson = signal<Person | null>(null);
  protected readonly personDialog = signal<PersonDialogTarget | null>(null);

  /** People an account can be made for: without one (or the edited
   * account's own), with the profile of every chosen role. A plain method —
   * `editingUserId` and `newUserRoles` are plain fields. */
  protected availablePeople(): Person[] {
    return this.people().filter(
      (p) =>
        (!p.account || p.account.userId === this.editingUserId) &&
        missingProfiles(p, this.chosenRoles()).length === 0,
    );
  }

  /** A new account whose person has an e-mail gets an invitation. */
  protected sendsInvite(): boolean {
    return this.editingUserId === null && !!this.userPerson()?.email;
  }

  /** "Chci nastavit heslo již teď" — the password field instead of just
   * the invitation. */
  protected showPassword = false;

  /** A new account without an invitation needs a password; otherwise the
   * field opens with the key. */
  protected passwordShown(): boolean {
    return (
      this.showPassword || (this.editingUserId === null && !this.sendsInvite())
    );
  }

  /** The edited account's "send a link to set the password" — the same
   * e-mail as the invitation. */
  protected sendInviteFromDialog(): void {
    const user = this.users().find((u) => u.id === this.editingUserId);
    const email = this.userPerson()?.email;
    if (user && email) {
      this.sendInvite({ ...user, email });
    }
  }

  /** Hidden again, the password isn't set (or changed). */
  protected togglePassword(): void {
    this.showPassword = !this.showPassword;
    if (!this.showPassword) {
      this.newPassword = '';
    }
  }

  // The "new…" fields below double as the dialog's form state for BOTH
  // adding and editing — one dialog, one set of fields, same pattern as
  // Rozvrh's "Nová hodina"/"Upravit hodinu" dialog. Which mode it's in is
  // just "is editingXId null" (see openAddXDialog/startEditX/saveX below).
  protected newSchoolClassName = '';
  protected newSchoolClassRocnik: number | null = null;
  protected newSchoolClassTeacherId: number | null = null;
  protected newSchoolClassTeachingLanguage = '';
  protected newSchoolClassHomeroomId: number | null = null;
  protected newSchoolClassRvpProgramCode: string | null = null;
  protected newSubjectName = '';
  protected newSubjectAbbreviation = '';
  protected newSubjectColor: string | null = null;
  protected newSubjectDoubleLessons = false;
  protected newClassroomName = '';
  protected newClassroomShortName = '';
  protected newClassroomType: ClassroomType | null = null;
  protected newClassroomCapacity: number | null = null;

  protected newStudentFirstName = '';
  protected newStudentLastName = '';
  /** Matrika of the class teacher's pupil dialog. */
  protected studentMatrika: StudentForm = emptyStudentForm(null);

  protected newUsername = '';
  protected newPassword = '';
  protected newUserRoles: number[] = [];
  protected newUserPersonId: number | null = null;

  protected filterStudentName = '';
  protected filterStudentSchoolClassId: number | null = null;
  protected filterStudentGender: Gender | null = null;
  protected filterUserRole: number | null = null;
  protected filterUserName = '';
  protected filterUserContact = '';
  protected filterParentName = '';
  protected filterParentChild = '';
  protected filterParentContact = '';

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

  /** "Rodiče" section's own dialog — null editingParentId means "assign a
   * parent" mode, where the parent is picked into addingParentId. It never
   * creates an account: a Parent-role account is created from the
   * Uživatelé tab like any other user, this dialog only links an existing
   * one to children. */
  protected editingParentId: number | null = null;
  protected addingParentId: number | null = null;
  protected editingParentChildIds: number[] = [];

  protected readonly schoolStreet = signal('');
  protected readonly schoolCity = signal('');
  protected readonly schoolPostalCode = signal('');
  protected readonly schoolDirectorName = signal('');
  protected readonly schoolEmail = signal('');
  protected readonly schoolPhone = signal('');
  protected readonly schoolBankAccountNumber = signal('');
  protected readonly schoolType = signal<SchoolType>('Elementary');
  protected readonly schoolTypeOptions: { value: SchoolType; label: string }[] =
    [
      { value: 'Elementary', label: 'Základní škola (žáci)' },
      { value: 'Secondary', label: 'Střední škola (studenti)' },
    ];

  protected readonly matrikaIzo = signal('');
  protected readonly matrikaRedIzo = signal('');
  protected matrikaContactName = '';
  protected matrikaContactPhone = '';
  protected matrikaContactEmail = '';
  protected matrikaCast = '01';
  protected readonly matrikaExportErrors = signal<string[]>([]);

  protected readonly teachingFocus = signal<{
    classId?: number;
    teacherId?: number;
  } | null>(null);

  protected showTeaching(focus: { classId?: number; teacherId?: number }) {
    this.navigate({ group: 'teaching', section: 'classes' });
    this.teachingFocus.set(focus);
  }

  /** A signal, not a plain field — this app has no zone.js polyfill, so a
   * plain-field write inside an RxJS subscribe callback doesn't reliably
   * schedule change detection on its own (it only "works" by coincidence
   * when some other signal write happens to run in the same tick). */

  constructor() {
    if (this.authApi.isAdmin()) {
      // accessLevel turns 'admin' only once the school's classes, subjects and
      // classrooms are in (see adminDataArrived) — rendering the cards empty
      // and then filling them shoved the lower cards down the page.
      this.reloadAll();
      this.loadMySchool();
      this.loadRoles();
      this.referenceApi.getCountries().subscribe({
        next: (countries) => this.countries.set(countries),
        error: (err) => console.error('getCountries failed', err),
      });
    } else if (this.authApi.can('Students')) {
      this.resolveClassTeacherAccess();
    } else {
      this.denyOrTeaching();
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
          this.denyOrTeaching();
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
        this.denyOrTeaching();
      },
    });
  }

  /** Without a class of their own, a teacher may still see Výuka. */
  private denyOrTeaching(): void {
    this.accessLevel.set(
      canSeeTeachingArea(this.authApi) ? 'teaching' : 'denied',
    );
  }

  /** Dispatches to whichever reload matches the resolved access level —
   * every mutation handler below (addStudent, addLink, ...) calls this
   * rather than `reloadAll`/`reloadClassTeacherData` directly, so the same
   * handler works correctly regardless of which view is active. */
  protected reload(): void {
    if (this.accessLevel() === 'classTeacher') {
      this.reloadClassTeacherData();
    } else {
      this.reloadAll();
      this.peopleTab()?.reload();
    }
  }

  private reloadClassTeacherData(): void {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        const scoped = students.filter((s) =>
          this.ownClassIds.includes(s.schoolClassId),
        );
        this.students.set(scoped);
        this.studentsLoaded.set(true);
        this.studentMatrika.schoolClassId ??= this.ownClassIds[0] ?? null;
      },
      error: (err) => {
        console.error('getStudents failed', err);
        this.studentsLoaded.set(true);
      },
    });
    // Already scoped server-side for a Teacher caller — see
    // ParentStudentsController.GetLinks.
    this.parentStudentsApi.getLinks().subscribe({
      next: (links) => this.links.set(links),
      error: (err) => console.error('getLinks failed', err),
    });
    this.parentsApi.getParents().subscribe({
      next: (parents) => {
        this.parents.set(parents);
        this.parentsLoaded.set(true);
      },
      error: (err) => {
        console.error('getParents failed', err);
        this.parentsLoaded.set(true);
      },
    });
  }

  /** p-autoComplete's completeMethod — backend ignores queries under 2 chars. */
  protected classTeacherName(classTeacherId: number | null): string {
    if (classTeacherId === null) {
      return '—';
    }
    return this.users().find((u) => u.id === classTeacherId)?.name ?? '—';
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
      .map((l) => l.parentName);
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

  protected filteredParents(): ParentUser[] {
    const name = foldForSearch(this.filterParentName.trim());
    const child = foldForSearch(this.filterParentChild.trim());
    const contact = this.filterParentContact.trim();
    return this.visibleParents().filter(
      (p) =>
        (!name || foldForSearch(p.name).includes(name)) &&
        (!child ||
          foldForSearch(this.parentChildNames(p.id)).includes(child)) &&
        (!contact || matchesContact(p, contact)),
    );
  }

  protected hasParentFilters(): boolean {
    return !!(
      this.filterParentName.trim() ||
      this.filterParentChild.trim() ||
      this.filterParentContact.trim()
    );
  }

  protected clearParentFilters(): void {
    this.filterParentName = '';
    this.filterParentChild = '';
    this.filterParentContact = '';
  }

  protected hasStudentFilters(): boolean {
    return (
      !!this.filterStudentName.trim() ||
      this.filterStudentSchoolClassId !== null ||
      this.filterStudentGender !== null
    );
  }

  protected clearStudentFilters(): void {
    this.filterStudentName = '';
    this.filterStudentSchoolClassId = null;
    this.filterStudentGender = null;
  }

  protected filteredStudents(): Student[] {
    const name = foldForSearch(this.filterStudentName.trim());
    return this.students().filter(
      (s) =>
        (!name ||
          foldForSearch(`${s.firstName} ${s.lastName}`).includes(name)) &&
        (this.filterStudentSchoolClassId === null ||
          s.schoolClassId === this.filterStudentSchoolClassId) &&
        (this.filterStudentGender === null ||
          s.gender === this.filterStudentGender),
    );
  }

  protected teacherClassNames(teacherId: number): string {
    return this.schoolClasses()
      .filter((c) => c.classTeacherId === teacherId)
      .map((c) => c.name)
      .join(', ');
  }

  protected studentPhotoUrl(student: Student): string | null {
    return student.photo
      ? this.photosApi.studentPhotoUrl(student.id, student.photo)
      : null;
  }

  protected userPhotoUrl(user: AppUser): string | null {
    return user.photo ? this.photosApi.userPhotoUrl(user.id, user.photo) : null;
  }

  protected parentPhotoUrl(parent: ParentUser): string | null {
    return parent.photo
      ? this.photosApi.userPhotoUrl(parent.id, parent.photo)
      : null;
  }

  /** Staff photos are the admin's to set; parents set their own. */
  protected isStaffUser(user: AppUser): boolean {
    return user.roles.some((r) => r.kind === 'Staff');
  }

  protected isPupilAccount(user: AppUser): boolean {
    return user.roles.some((r) => r.kind === 'Student');
  }

  protected studentPhotoChanged(change: {
    studentId: number;
    photo: string | null;
  }): void {
    this.students.update((all) =>
      all.map((s) =>
        s.id === change.studentId ? { ...s, photo: change.photo } : s,
      ),
    );
  }

  protected userPhotoChanged(change: {
    userId: number;
    photo: string | null;
  }): void {
    this.users.update((all) =>
      all.map((u) =>
        u.id === change.userId ? { ...u, photo: change.photo } : u,
      ),
    );
  }

  private adminRoleId(): number | undefined {
    return this.schoolRoles().find((r) => r.builtIn === 'Admin')?.id;
  }

  /** The roles picked in the account dialog. */
  private chosenRoles(): RoleRef[] {
    return this.schoolRoles().filter((r) => this.newUserRoles.includes(r.id));
  }

  protected hasUserFilters(): boolean {
    return (
      !!this.filterUserName.trim() ||
      !!this.filterUserContact.trim() ||
      this.filterUserRole !== null
    );
  }

  protected clearUserFilters(): void {
    this.filterUserName = '';
    this.filterUserContact = '';
    this.filterUserRole = null;
  }

  protected filteredUsers(): AppUser[] {
    const name = foldForSearch(this.filterUserName.trim());
    const contact = this.filterUserContact.trim();
    return this.users().filter(
      (u) =>
        (this.filterUserRole === null ||
          u.roles.some((r) => r.id === this.filterUserRole)) &&
        (!name || foldForSearch(`${u.name} ${u.username}`).includes(name)) &&
        (!contact || matchesContact(u, contact)),
    );
  }

  /** Parent accounts the "Přidat" dialog can offer — the ones not already
   * in the Rodiče list (those are edited via their own row instead). */
  protected readonly assignableParents = computed(() => {
    const listed = new Set(this.visibleParents().map((p) => p.id));
    return this.parents().filter((p) => !listed.has(p.id));
  });

  /** Classes, subjects and classrooms (the first tab) still to arrive on
   * the initial load; at zero the page renders. Errors count too, so a
   * failed request doesn't leave the page stuck on "Načítání…". */
  private initialAdminRequests = 3;

  private adminDataArrived(): void {
    if (--this.initialAdminRequests === 0) {
      this.accessLevel.set('admin');
    }
  }

  protected loadRoles(): void {
    this.rolesApi.getRoles().subscribe({
      next: (roles) => this.schoolRoles.set(roles),
      error: (err) => console.error('getRoles failed', err),
    });
  }

  private reloadAll(): void {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        this.studentsLoaded.set(true);
      },
      error: (err) => {
        console.error('getStudents failed', err);
        this.studentsLoaded.set(true);
      },
    });
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (schoolClasses) => {
        this.schoolClasses.set(schoolClasses);
        this.adminDataArrived();
      },
      error: (err) => {
        console.error('getSchoolClasses failed', err);
        this.adminDataArrived();
      },
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => {
        this.subjects.set(subjects);
        this.adminDataArrived();
      },
      error: (err) => {
        console.error('getSubjects failed', err);
        this.adminDataArrived();
      },
    });
    this.classroomsApi.getClassrooms().subscribe({
      next: (classrooms) => {
        this.classrooms.set(classrooms);
        this.adminDataArrived();
      },
      error: (err) => {
        console.error('getClassrooms failed', err);
        this.adminDataArrived();
      },
    });
    this.usersApi.getUsers().subscribe({
      next: (users) => {
        this.users.set(users);
        this.usersLoaded.set(true);
      },
      error: (err) => {
        console.error('getUsers failed', err);
        this.usersLoaded.set(true);
      },
    });
    this.parentStudentsApi.getLinks().subscribe({
      next: (links) => this.links.set(links),
      error: (err) => console.error('getLinks failed', err),
    });
    this.parentsApi.getParents().subscribe({
      next: (parents) => {
        this.parents.set(parents);
        this.parentsLoaded.set(true);
      },
      error: (err) => {
        console.error('getParents failed', err);
        this.parentsLoaded.set(true);
      },
    });
  }

  private loadRvpPrograms(): void {
    this.teachingApi.getRvpPrograms().subscribe({
      next: (programs) => this.rvpPrograms.set(programs),
      error: (err) => console.error('getRvpPrograms failed', err),
    });
  }

  protected openAddClassDialog(): void {
    this.editingSchoolClassId = null;
    this.newSchoolClassName = '';
    this.newSchoolClassRocnik = null;
    this.newSchoolClassTeacherId = null;
    this.newSchoolClassTeachingLanguage = '';
    this.newSchoolClassHomeroomId = null;
    this.newSchoolClassRvpProgramCode = null;
    this.loadRvpPrograms();
    this.notify.clear();
    this.newClassDialogVisible.set(true);
  }

  protected startEditSchoolClass(schoolClass: SchoolClass): void {
    this.editingSchoolClassId = schoolClass.id;
    this.newSchoolClassName = schoolClass.name;
    this.newSchoolClassRocnik = schoolClass.rocnik;
    this.newSchoolClassTeacherId = schoolClass.classTeacherId;
    this.newSchoolClassTeachingLanguage = schoolClass.teachingLanguage ?? '';
    this.newSchoolClassHomeroomId = schoolClass.homeroomId;
    this.newSchoolClassRvpProgramCode = schoolClass.rvpProgramCode;
    this.loadRvpPrograms();
    this.notify.clear();
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
      homeroomId: this.newSchoolClassHomeroomId ?? undefined,
      rvpProgramCode: this.newSchoolClassRvpProgramCode ?? undefined,
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
    this.newSubjectDoubleLessons = false;
    this.newSubjectClassrooms.set([]);
    this.notify.clear();
    this.newSubjectDialogVisible.set(true);
  }

  protected startEditSubject(subject: Subject): void {
    this.editingSubjectId = subject.id;
    this.newSubjectName = subject.name;
    this.newSubjectAbbreviation = subject.abbreviation ?? '';
    this.newSubjectColor = subject.color;
    this.newSubjectDoubleLessons = subject.doubleLessons;
    this.newSubjectClassrooms.set(
      [...subject.classrooms]
        .sort((a, b) => a.position - b.position)
        .map((c) =>
          c.classroomId !== null ? `room:${c.classroomId}` : `type:${c.type}`,
        ),
    );
    this.notify.clear();
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
      doubleLessons: this.newSubjectDoubleLessons,
      classrooms: [...new Set(this.newSubjectClassrooms())]
        .filter((c) => c !== null)
        .map((c) =>
          c.startsWith('room:')
            ? { classroomId: Number(c.slice(5)) }
            : { type: c.slice(5) as ClassroomType },
        ),
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

  protected setSubjectClassroom(index: number, value: string | null): void {
    this.newSubjectClassrooms.update((list) =>
      list.map((c, i) => (i === index ? value : c)),
    );
  }

  protected addSubjectClassroom(): void {
    this.newSubjectClassrooms.update((list) => [...list, null]);
  }

  protected removeSubjectClassroom(index: number): void {
    this.newSubjectClassrooms.update((list) =>
      list.filter((_, i) => i !== index),
    );
  }

  /** Moves it one place up, to be tried earlier. */
  protected raiseSubjectClassroom(index: number): void {
    this.newSubjectClassrooms.update((list) => {
      const next = [...list];
      [next[index - 1], next[index]] = [next[index], next[index - 1]];
      return next;
    });
  }

  /** "Tělocvična, jakákoli tělocvična" — the subject's classrooms in order. */
  protected subjectClassroomsLabel(subject: Subject): string {
    if (subject.classrooms.length === 0) {
      return '—';
    }
    return [...subject.classrooms]
      .sort((a, b) => a.position - b.position)
      .map((c) =>
        c.classroomId !== null
          ? this.classroomName(c.classroomId)
          : `jakákoli: ${this.classroomTypeLabel(c.type).toLowerCase()}`,
      )
      .join(', ');
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
    this.newClassroomShortName = '';
    this.newClassroomType = null;
    this.newClassroomCapacity = null;
    this.notify.clear();
    this.newClassroomDialogVisible.set(true);
  }

  protected startEditClassroom(classroom: Classroom): void {
    this.editingClassroomId = classroom.id;
    this.newClassroomName = classroom.name;
    this.newClassroomShortName = classroom.shortName ?? '';
    this.newClassroomType = classroom.type;
    this.newClassroomCapacity = classroom.capacity;
    this.notify.clear();
    this.newClassroomDialogVisible.set(true);
  }

  protected saveClassroom(): void {
    if (!this.newClassroomName.trim()) {
      return;
    }
    const request = {
      name: this.newClassroomName,
      shortName: this.newClassroomShortName.trim() || undefined,
      type: this.newClassroomType ?? undefined,
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
    this.studentMatrika = emptyStudentForm(
      this.ownClassIds[0] ?? this.schoolClasses()[0]?.id ?? null,
    );
    this.editingStudentParentIds = [];
    this.notify.clear();
    this.newStudentDialogVisible.set(true);
  }

  protected startEditStudent(student: Student): void {
    this.editingStudentId = student.id;
    this.editingStudentParentIds = this.links()
      .filter((l) => l.studentId === student.id)
      .map((l) => l.userId);
    this.newStudentFirstName = student.firstName;
    this.newStudentLastName = student.lastName;
    this.studentMatrika = studentFormOf(student);
    this.notify.clear();
    this.newStudentDialogVisible.set(true);
  }

  protected saveStudent(): void {
    if (
      !this.newStudentFirstName.trim() ||
      !this.newStudentLastName.trim() ||
      !this.studentMatrika.schoolClassId
    ) {
      return;
    }
    const request = {
      firstName: this.newStudentFirstName,
      lastName: this.newStudentLastName,
      ...matrikaOf(this.studentMatrika),
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

  protected readonly leaveStaff = signal<StaffLeaveTarget | null>(null);

  /** Only a pupil without history (one added by mistake) can be deleted;
   * for one with grades, absences… the server answers 409 with what's
   * there (an admin ends the pupil's education in Osoby instead). */
  protected deleteStudent(student: Student): void {
    const type = this.authApi.getSchoolType();
    this.confirmation.confirm({
      header: localizeStudentTerm('Smazat žáka', type),
      message: localizeStudentTerm(
        `Opravdu smazat žáka ${student.firstName} ${student.lastName}? Smazat jde jen žáka bez záznamů (založeného omylem), jinak se ukončuje jeho vzdělávání.`,
        type,
      ),
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.studentsApi.deleteStudent(student.id).subscribe({
          next: () => this.reload(),
          error: (err) => {
            if (err instanceof HttpErrorResponse && err.status === 409) {
              this.notify.warn(String(err.error));
            } else {
              this.showError('deleteStudent failed', err);
            }
          },
        }),
    });
  }

  protected openAddUserDialog(): void {
    this.editingUserId = null;
    this.newUsername = '';
    this.newPassword = '';
    this.newUserRoles = [];
    this.showPassword = false;
    this.newUserPersonId = null;
    this.userPerson.set(null);
    this.loadPeople();
    this.notify.clear();
    this.newUserDialogVisible.set(true);
  }

  private loadPeople(): void {
    this.peopleApi.getPeople().subscribe({
      next: (people) => this.people.set(people),
      error: (err) => console.error('getPeople failed', err),
    });
  }

  /** "Vytvořit účet" from Osoby: roles after the person's profiles. */
  protected openCreateAccountFor(person: Person): void {
    this.openAddUserDialog();
    const builtIn = (name: string) =>
      this.schoolRoles().find((r) => r.builtIn === name)?.id;
    this.newUserRoles = (
      person.student
        ? [builtIn('Student')]
        : [
            ...(person.teacher ? [builtIn('Teacher')] : []),
            ...(person.parent ? [builtIn('Parent')] : []),
          ]
    ).filter((id): id is number => id !== undefined);
    this.setUserPerson(person);
  }

  /** Picking the person suggests a username ("jan.novak") for a new
   * account; `null` clears the choice. */
  protected setUserPerson(person: Person | number | null): void {
    const picked =
      typeof person === 'number'
        ? (this.people().find((p) => p.id === person) ?? null)
        : person;
    this.userPerson.set(picked);
    this.newUserPersonId = picked?.id ?? null;
    if (picked && this.editingUserId === null && !this.newUsername.trim()) {
      this.newUsername = suggestUsername(picked.firstName, picked.lastName);
    }
  }

  /** What the chosen person lacks for the chosen roles. */
  protected userPersonProblems(): string[] {
    const person = this.userPerson();
    if (!person) {
      return ['Vyberte nebo založte osobu.'];
    }
    return missingProfiles(person, this.chosenRoles()).map(
      (role) =>
        `Role ${role.name} potřebuje profil ${PROFILE_NAMES[profileOf(role)!]} — doplňte ho tužkou.`,
    );
  }

  protected openNewPersonForUser(): void {
    const profiles = this.chosenRoles()
      .map(profileOf)
      .filter((profile): profile is PersonProfile => !!profile);
    this.personDialog.set({ id: null, profiles });
  }

  /** The person dialog saved — in the account dialog it's the chosen one. */
  protected onPersonSaved(person: Person): void {
    this.personDialog.set(null);
    this.people.update((all) => [
      ...all.filter((p) => p.id !== person.id),
      person,
    ]);
    if (this.newUserDialogVisible()) {
      this.setUserPerson(person);
    }
    this.reload();
  }

  protected classroomTypeLabel(type: ClassroomType | null): string {
    return type ? CLASSROOM_TYPE_LABELS[type] : '—';
  }

  protected hasAccount(studentId: number): boolean {
    return this.users().some((u) => u.studentId === studentId);
  }

  protected startEditUser(user: AppUser): void {
    this.editingUserId = user.id;
    this.newUsername = user.username;
    this.newUserRoles = user.roles.map((r) => r.id);
    this.newPassword = ''; // left blank = keep the current password
    this.showPassword = false;
    this.newUserPersonId = user.personId;
    this.userPerson.set(null);
    this.peopleApi.getPeople().subscribe({
      next: (people) => {
        this.people.set(people);
        this.setUserPerson(user.personId);
      },
      error: (err) => console.error('getPeople failed', err),
    });
    this.notify.clear();
    this.newUserDialogVisible.set(true);
  }

  protected saveUser(): void {
    if (!this.newUsername.trim() || this.userPersonProblems().length > 0) {
      return;
    }

    if (this.editingUserId === null) {
      const request: CreateUserRequest = {
        username: this.newUsername,
        password: this.newPassword || undefined,
        roleIds: this.newUserRoles,
        personId: this.newUserPersonId,
      };
      this.usersApi.addUser(request).subscribe({
        next: (user) => {
          this.newUserDialogVisible.set(false);
          this.notifyInviteOutcome(user);
          this.reload();
        },
        error: (err) => this.showError('addUser failed', err),
      });
      return;
    }

    const request: UpdateUserRequest = {
      username: this.newUsername,
      roleIds: this.newUserRoles,
      personId: this.newUserPersonId,
      password: this.newPassword || undefined,
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

  /** Editing your own account keeps the Admin role even if its chip gets
   * removed (the option itself is disabled, see ownRoleOptions). */
  protected onUserRolesChange(roles: number[]): void {
    const admin = this.adminRoleId();
    this.newUserRoles =
      this.editingUserId === this.myUserId &&
      admin !== undefined &&
      !roles.includes(admin)
        ? [admin, ...roles]
        : roles;
  }

  /** A new account with an e-mail gets its invitation right away; the
   * account exists either way, a failed invite can be resent (envelope). */
  private notifyInviteOutcome(user: AppUser): void {
    switch (user.invite) {
      case 'Sent':
        this.demoEmails.sent(
          `Uživatel vytvořen, pozvánka odeslána na ${user.email}.`,
        );
        break;
      case 'NotConfigured':
        this.notify.warn(
          'Uživatel vytvořen, ale pozvánka neodešla — e-mailové pozvánky nejsou v tomhle prostředí nakonfigurované.',
        );
        break;
      case 'Failed':
        this.notify.warn(
          'Uživatel vytvořen, ale pozvánku se nepodařilo odeslat. Zkuste to znovu tlačítkem obálky.',
        );
        break;
    }
  }

  /** Backend rejects (400) when the user has no e-mail on file, or (503)
   * when this environment has no e-mail infrastructure configured. */
  protected sendInvite(user: AppUser): void {
    this.usersApi.sendInvite(user.id).subscribe({
      next: () => this.demoEmails.sent(`Pozvánka odeslána na ${user.email}.`),
      error: (err) => this.showError('sendInvite failed', err),
    });
  }

  /** Backend rejects (400) deleting yourself or the school's last Admin.
   * The person stays in Osoby, with their children. */
  protected deleteUser(user: AppUser): void {
    const name = user.name || user.username;
    this.confirmation.confirm({
      header: 'Smazat uživatele',
      message: `Opravdu smazat účet ${name}? Osoba i s profily zůstane v záložce Osoby.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Smazat',
      rejectLabel: 'Zrušit',
      acceptButtonProps: { severity: 'danger' },
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.usersApi.deleteUser(user.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.showError('deleteUser failed', err),
        }),
    });
  }

  /** Deactivating ends the account's sessions; the data stays until the
   * school's retention period for staff (Administrace → Archiv a výmaz údajů). */
  protected toggleActive(user: AppUser): void {
    const name = user.name || user.username;
    if (user.deactivatedAt) {
      this.usersApi.activate(user.id).subscribe({
        next: () => this.reload(),
        error: (err) => this.showError('activate failed', err),
      });
      return;
    }
    this.confirmation.confirm({
      header: 'Deaktivovat účet',
      message: `Deaktivovat účet ${name}? Přestane fungovat přihlášení, údaje zůstanou a smažou se podle pravidel v záložce Archiv a výmaz údajů. Účet jde znovu aktivovat.`,
      icon: 'pi pi-exclamation-triangle',
      acceptLabel: 'Deaktivovat',
      rejectLabel: 'Zrušit',
      rejectButtonProps: { severity: 'secondary', outlined: true },
      accept: () =>
        this.usersApi.deactivate(user.id).subscribe({
          next: () => this.reload(),
          error: (err) => this.showError('deactivate failed', err),
        }),
    });
  }

  protected openAssignParentDialog(): void {
    this.editingParentId = null;
    this.addingParentId = null;
    this.editingParentChildIds = [];
    this.notify.clear();
    this.editParentChildrenDialogVisible.set(true);
  }

  protected openEditParentChildrenDialog(parent: AppUser): void {
    this.editingParentId = parent.id;
    this.editingParentChildIds = this.links()
      .filter((l) => l.userId === parent.id)
      .map((l) => l.studentId);
    this.notify.clear();
    this.editParentChildrenDialogVisible.set(true);
  }

  protected saveParentChildren(): void {
    const parentId = this.editingParentId ?? this.addingParentId;
    if (parentId === null) {
      return;
    }
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
        this.addingParentId = null;
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
        this.schoolBankAccountNumber.set(
          school.bankAccountNumber ? formatIban(school.bankAccountNumber) : '',
        );
        this.schoolType.set(school.type);
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

  /** The type travels in the access token (`schoolType` claim) — fetch a
   * fresh one and reload, so every already-rendered "žák"/"student" text
   * (pure `term` pipe) switches at once. */
  protected saveSchoolType(): void {
    this.mySchoolApi
      .setType({ type: this.schoolType() })
      .pipe(switchMap(() => this.authApi.refresh()))
      .subscribe({
        next: () => window.location.reload(),
        error: (err) => this.showError('setSchoolType failed', err),
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

  /** The account Platby's client-generated SPAYD QR codes pay into.
   * Separate from saveSchoolDetails: it's payment plumbing, not an
   * institutional-document field. The server accepts an IBAN or the Czech
   * "předčíslí-číslo/kód banky" form and stores the IBAN. */
  protected saveSchoolBankAccount(): void {
    this.mySchoolApi
      .setBankAccount({
        bankAccountNumber: this.schoolBankAccountNumber() || null,
      })
      .subscribe({
        next: (school) => {
          this.notify.success(
            school.bankAccountNumber
              ? `Uloženo jako IBAN ${formatIban(school.bankAccountNumber)}.`
              : 'Účet odebrán.',
          );
          this.loadMySchool();
        },
        error: (err) => this.showError('setBankAccount failed', err),
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
    this.notify.actionFailed(context, err);
  }
}

/** The roles of `roles` whose profile the person lacks. */
function missingProfiles(person: Person, roles: RoleRef[]): RoleRef[] {
  return roles.filter((role) => {
    const profile = profileOf(role);
    return profile !== null && !person[profile];
  });
}

/** "Jan Novák" -> "jan.novak" (no diacritics, letters/digits only). */
function suggestUsername(firstName: string, lastName: string): string {
  const part = (text: string) => foldForSearch(text).replace(/[^a-z0-9]/g, '');
  return [part(firstName), part(lastName)].filter((p) => p).join('.');
}
