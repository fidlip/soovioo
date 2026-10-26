import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Checkbox } from '@openng/optimus-ui/checkbox';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { MultiSelect } from '@openng/optimus-ui/multiselect';
import {
  Country,
  PeopleApiService,
  Person,
  ReferenceApiService,
  SavePersonRequest,
  SchoolClass,
  SchoolClassesApiService,
  Student,
  StudentsApiService,
} from '@soovioo/shared';
import { FORM_FIELDS } from '../forms';
import { NotifyService } from '../notify.service';
import { TermPipe } from '../term.pipe';
import {
  StudentFields,
  StudentForm,
  emptyStudentForm,
  matrikaOf,
  studentFormOf,
} from './student-fields';

export type PersonProfile = 'teacher' | 'parent' | 'student';

/** null id = a new person, with `profiles` ticked to start with. */
export interface PersonDialogTarget {
  id: number | null;
  profiles?: PersonProfile[];
}

/** Administrace → Osoby and the account dialog: a person's name, contacts
 * and profiles (učitel, rodič, žák) in one form. Open while `target` is set. */
@Component({
  selector: 'app-person-dialog',
  imports: [
    Checkbox,
    Dialog,
    FormsModule,
    InputText,
    MultiSelect,
    StudentFields,
    TermPipe,
    ...FORM_FIELDS,
  ],
  templateUrl: './person-dialog.html',
})
export class PersonDialog {
  private readonly peopleApi = inject(PeopleApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly schoolClassesApi = inject(SchoolClassesApiService);
  private readonly referenceApi = inject(ReferenceApiService);
  private readonly notify = inject(NotifyService);

  readonly target = input<PersonDialogTarget | null>(null);
  readonly closed = output<void>();
  readonly saved = output<Person>();

  protected readonly visible = computed(() => this.target() !== null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  /** The person as loaded — null for a new one. */
  protected readonly person = signal<Person | null>(null);
  protected readonly classes = signal<SchoolClass[]>([]);
  protected readonly countries = signal<Country[]>([]);
  private readonly students = signal<Student[]>([]);
  protected readonly childOptions = computed(() =>
    this.students()
      .filter((s) => s.id !== this.person()?.student?.id)
      .map((s) => ({
        id: s.id,
        label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
      })),
  );

  protected firstName = '';
  protected lastName = '';
  protected phone = '';
  protected email = '';
  protected isTeacher = false;
  protected qualification = '';
  protected isParent = false;
  protected childIds: number[] = [];
  protected isStudent = false;
  protected studentForm: StudentForm = emptyStudentForm(null);

  /** A profile the account's role needs (or a pupil's) can't be unticked
   * here — the server refuses it too. */
  protected readonly locked = computed(() => {
    const p = this.person();
    const roles = p?.account?.roles ?? [];
    return {
      teacher: !!p?.teacher && roles.includes('Teacher'),
      parent: !!p?.parent && roles.includes('Parent'),
      student: !!p?.student,
    };
  });

  constructor() {
    effect(() => {
      const target = this.target();
      if (target) {
        this.open(target);
      }
    });
  }

  private open(target: PersonDialogTarget): void {
    this.notify.clear();
    this.person.set(null);
    this.firstName = '';
    this.lastName = '';
    this.phone = '';
    this.email = '';
    this.isTeacher = target.profiles?.includes('teacher') ?? false;
    this.qualification = '';
    this.isParent = target.profiles?.includes('parent') ?? false;
    this.childIds = [];
    this.isStudent = target.profiles?.includes('student') ?? false;
    this.studentForm = emptyStudentForm(null);
    this.loadLists();
    if (target.id === null) {
      return;
    }
    this.loading.set(true);
    this.peopleApi.getPerson(target.id).subscribe({
      next: (person) => {
        this.person.set(person);
        this.firstName = person.firstName;
        this.lastName = person.lastName;
        this.phone = person.phone ?? '';
        this.email = person.email ?? '';
        this.isTeacher = !!person.teacher;
        this.qualification = person.teacher?.qualification ?? '';
        this.isParent = !!person.parent;
        this.childIds = person.parent?.children.map((c) => c.studentId) ?? [];
        this.isStudent = !!person.student;
        if (person.student) {
          this.studentForm = studentFormOf(person.student);
        }
        this.loading.set(false);
      },
      error: (err) => {
        this.loading.set(false);
        this.notify.actionFailed('getPerson failed', err);
      },
    });
  }

  private loadLists(): void {
    this.schoolClassesApi.getSchoolClasses().subscribe({
      next: (classes) => {
        this.classes.set(classes);
        this.studentForm.schoolClassId ??= classes[0]?.id ?? null;
      },
      error: (err) => console.error('getSchoolClasses failed', err),
    });
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    if (this.countries().length === 0) {
      this.referenceApi.getCountries().subscribe({
        next: (countries) => this.countries.set(countries),
        error: (err) => console.error('getCountries failed', err),
      });
    }
  }

  protected save(): void {
    const request: SavePersonRequest = {
      firstName: this.firstName.trim(),
      lastName: this.lastName.trim(),
      phone: this.phone.trim() || undefined,
      email: this.email.trim() || undefined,
      teacher: this.isTeacher
        ? { qualification: this.qualification.trim() || undefined }
        : undefined,
      parent: this.isParent ? { childIds: this.childIds } : undefined,
      student: this.isStudent ? matrikaOf(this.studentForm) : undefined,
    };
    const id = this.person()?.id ?? null;
    this.busy.set(true);
    (id === null
      ? this.peopleApi.addPerson(request)
      : this.peopleApi.updatePerson(id, request)
    ).subscribe({
      next: (person) => {
        this.busy.set(false);
        this.saved.emit(person);
      },
      error: (err) => {
        this.busy.set(false);
        this.notify.actionFailed('savePerson failed', err);
      },
    });
  }
}
