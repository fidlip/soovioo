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
  Absence,
  AbsencesApiService,
  AuthApiService,
  DateFormatPipe,
  Student,
  StudentsApiService,
} from '@soovioo/shared';
import { HIGHLIGHT_STYLES, Highlight, onLinkedItem } from '../../linked-item';
import { NavBarComponent } from '../nav-bar/nav-bar.component';

@Component({
  selector: 'ns-absences',
  templateUrl: './absences.component.html',
  imports: [NativeScriptCommonModule, NavBarComponent, DateFormatPipe],
  schemas: [NO_ERRORS_SCHEMA],
  styles: [HIGHLIGHT_STYLES],
})
export class AbsencesComponent {
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly absences = signal<Absence[]>([]);
  protected readonly students = signal<Student[]>([]);
  protected selectedStudentIndex = 0;
  protected readonly highlight = new Highlight();
  private readonly list = viewChild<ElementRef<ListView>>('list');

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => this.students.set(students),
      error: (err) => console.error('getStudents failed', err),
    });
    this.reload();
    onLinkedItem('absence', (id) => this.reload(id));
  }

  protected canManageAbsences(): boolean {
    return this.authApi.can('AbsencesWrite');
  }

  protected canSubmitExcuse(): boolean {
    return this.authApi.can('AbsencesExcuse');
  }

  protected addAbsence(): void {
    const student = this.students()[this.selectedStudentIndex];
    if (!student) {
      return;
    }
    this.absencesApi
      .addAbsence({
        studentId: student.id,
        date: new Date().toISOString().slice(0, 10),
      })
      .subscribe({
        next: () => this.reload(),
        error: (err) => console.error('addAbsence failed', err),
      });
  }

  protected submitExcuse(absence: Absence): void {
    this.absencesApi.submitExcuse(absence.id, 'Nemoc').subscribe({
      next: () => this.reload(),
      error: (err) => console.error('submitExcuse failed', err),
    });
  }

  protected review(absence: Absence, approve: boolean): void {
    this.absencesApi.reviewAbsence(absence.id, approve).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('reviewAbsence failed', err),
    });
  }

  /** `linkedId`: the absence a notification opened, shown once loaded. */
  private reload(linkedId?: number): void {
    this.absencesApi.getAbsences().subscribe({
      next: (absences) => {
        this.absences.set(absences);
        if (linkedId) {
          this.highlight.show(
            this.list()?.nativeElement,
            absences.map((a) => a.id),
            linkedId,
          );
        }
      },
      error: (err) => console.error('getAbsences failed', err),
    });
  }
}
