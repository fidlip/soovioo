import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Card } from '@openng/optimus-ui/card';
import { Select } from '@openng/optimus-ui/select';
import {
  ClassDirectory,
  ClassDirectoryApiService,
  Classmate,
  DirectoryParent,
  DirectoryTeacher,
  PhotosApiService,
  Student,
  StudentsApiService,
} from '@soovioo/shared';
import { Avatar } from '../avatar/avatar';
import { TermPipe } from '../term.pipe';
import { FloatField } from '../forms';

/** Lidé → Třída for parents and pupils: the class's teachers and — as far as
 * everyone's "Viditelnost údajů" shares them — their contacts, the class
 * list and the other parents. Sections the API leaves out aren't shown. */
@Component({
  selector: 'app-class-page',
  imports: [FloatField, FormsModule, Card, Select, Avatar, TermPipe],
  templateUrl: './class-page.html',
})
export class ClassPage {
  private readonly classDirectoryApi = inject(ClassDirectoryApiService);
  private readonly studentsApi = inject(StudentsApiService);
  private readonly photosApi = inject(PhotosApiService);

  /** A parent's children (a pupil: just themselves) — the picker shows
   * only with more than one. */
  protected readonly students = signal<Student[]>([]);
  protected readonly studentOptions = computed(() =>
    this.students().map((s) => ({
      label: `${s.firstName} ${s.lastName} (${s.schoolClassName})`,
      value: s.id,
    })),
  );
  protected readonly selectedStudentId = signal<number | null>(null);
  protected readonly directory = signal<ClassDirectory | null>(null);
  protected readonly loaded = signal(false);

  constructor() {
    this.studentsApi.getStudents().subscribe({
      next: (students) => {
        this.students.set(students);
        if (students.length > 0) {
          this.select(students[0].id);
        } else {
          this.loaded.set(true);
        }
      },
      error: (err) => {
        console.error('getStudents failed', err);
        this.loaded.set(true);
      },
    });
  }

  protected select(studentId: number): void {
    this.selectedStudentId.set(studentId);
    this.classDirectoryApi.getClassDirectory(studentId).subscribe({
      next: (directory) => {
        this.directory.set(directory);
        this.loaded.set(true);
      },
      error: (err) => {
        console.error('getClassDirectory failed', err);
        this.loaded.set(true);
      },
    });
  }

  protected teacherPhotoUrl(teacher: DirectoryTeacher): string | null {
    return teacher.photo
      ? this.photosApi.userPhotoUrl(teacher.userId, teacher.photo)
      : null;
  }

  protected classmatePhotoUrl(classmate: Classmate): string | null {
    return classmate.photo
      ? this.photosApi.studentPhotoUrl(classmate.studentId, classmate.photo)
      : null;
  }

  protected parentPhotoUrl(parent: DirectoryParent): string | null {
    return parent.photo && parent.userId
      ? this.photosApi.userPhotoUrl(parent.userId, parent.photo)
      : null;
  }
}
