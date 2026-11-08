import {
  Component,
  ElementRef,
  NO_ERRORS_SCHEMA,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { NativeScriptCommonModule } from '@nativescript/angular';
import type { ListView } from '@nativescript/core';
import {
  AuthApiService,
  Grade,
  GradesApiService,
  Student,
  StudentsApiService,
  Subject,
  SubjectsApiService,
} from '@soovioo/shared';
import {
  HIGHLIGHT_STYLES,
  Highlight,
  linkedItemGone,
  onLinkedItem,
} from '../../linked-item';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-home',
  templateUrl: './home.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent],
  schemas: [NO_ERRORS_SCHEMA],
  styles: [HIGHLIGHT_STYLES],
})
export class HomeComponent {
  private readonly gradesApi = inject(GradesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly subjectsApi = inject(SubjectsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly grades = signal<Grade[]>([]);
  protected readonly highlight = new Highlight();
  private readonly list = viewChild<ElementRef<ListView>>('list');
  private firstStudent: Student | undefined;
  private firstSubject: Subject | undefined;

  constructor() {
    this.reload();
    onLinkedItem('grade', (id) => this.reload(id));
    this.studentsApi.getStudents().subscribe({
      next: (students) => (this.firstStudent = students[0]),
      error: (err) => console.error('getStudents failed', err),
    });
    this.subjectsApi.getSubjects().subscribe({
      next: (subjects) => (this.firstSubject = subjects[0]),
      error: (err) => console.error('getSubjects failed', err),
    });
  }

  protected canAddGrade(): boolean {
    return this.authApi.can('GradesWrite');
  }

  protected addDemoGrade(): void {
    if (!this.firstStudent || !this.firstSubject) {
      return;
    }
    this.gradesApi
      .addGrade({
        studentId: this.firstStudent.id,
        subjectId: this.firstSubject.id,
        value: 1,
        weight: 2,
        note: 'Pololetní práce',
      })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addGrade failed', err),
      });
  }

  /** `linkedId`: the grade a notification opened, shown once loaded. */
  private reload(linkedId?: number): void {
    this.gradesApi.getGrades({ pageSize: 100 }).subscribe({
      next: (result) => {
        this.grades.set(result.items);
        if (!linkedId) {
          return;
        }
        const ids = result.items.map((g) => g.id);
        if (ids.includes(linkedId)) {
          this.highlight.show(this.list()?.nativeElement, ids, linkedId);
        } else {
          // Maybe just further down than the list goes.
          this.gradesApi
            .getGrade(linkedId)
            .subscribe({ error: () => linkedItemGone() });
        }
      },
      error: (err) => console.error('getGrades failed', err),
    });
  }
}
