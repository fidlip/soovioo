import { Component, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Paginator } from '@openng/optimus-ui/paginator';
import { Select } from '@openng/optimus-ui/select';
import { PaginatorState } from '@openng/optimus-ui/types/paginator';
import {
  AuthApiService,
  PhotosApiService,
  SchoolDirectory,
  SchoolDirectoryApiService,
  SchoolPerson,
  SchoolPersonKind,
  localizeStudentTerm,
} from '@soovioo/shared';
import { Subject, debounceTime } from 'rxjs';
import { Avatar } from '../avatar/avatar';
import { FORM_FIELDS, FloatField } from '../forms';

/** Lidé → Škola: everyone at the school as far as their "Viditelnost
 * údajů" reaches the viewer — the API leaves the rest out. Searched,
 * filtered and paged on the server. */
@Component({
  selector: 'app-school-directory',
  imports: [
    FloatField,
    ...FORM_FIELDS,
    FormsModule,
    InputText,
    Paginator,
    Select,
    Avatar,
  ],
  templateUrl: './school-directory.html',
})
export class SchoolDirectoryList {
  private readonly api = inject(SchoolDirectoryApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly authApi = inject(AuthApiService);

  protected readonly directory = signal<SchoolDirectory | null>(null);
  protected search = '';
  protected readonly kind = signal<SchoolPersonKind | null>(null);
  protected readonly classId = signal<number | null>(null);
  protected readonly page = signal(1);
  protected readonly pageSize = signal(50);

  protected readonly kindOptions = computed(() => {
    const pupils = localizeStudentTerm('Žáci', this.authApi.getSchoolType());
    return [
      { label: 'Všichni', value: null },
      { label: 'Zaměstnanci', value: 'Staff' },
      { label: pupils, value: 'Student' },
      ...(this.directory()?.showsParents
        ? [{ label: 'Rodiče', value: 'Parent' }]
        : []),
    ];
  });
  protected readonly classOptions = computed(() => [
    { label: 'Všechny třídy', value: null },
    ...(this.directory()?.classes ?? []).map((c) => ({
      label: c.name,
      value: c.id,
    })),
  ]);

  private readonly searchChanges = new Subject<void>();

  constructor() {
    this.searchChanges
      .pipe(debounceTime(300), takeUntilDestroyed())
      .subscribe(() => this.reload(1));
    this.reload(1);
  }

  protected onSearch(): void {
    this.searchChanges.next();
  }

  protected setKind(kind: SchoolPersonKind | null): void {
    this.kind.set(kind);
    this.reload(1);
  }

  protected setClass(classId: number | null): void {
    this.classId.set(classId);
    this.reload(1);
  }

  protected onPageChange(event: PaginatorState): void {
    this.pageSize.set(event.rows ?? this.pageSize());
    this.reload((event.page ?? 0) + 1);
  }

  private reload(page: number): void {
    this.page.set(page);
    this.api
      .getSchoolDirectory({
        search: this.search,
        kind: this.kind(),
        classId: this.classId(),
        page,
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (directory) => this.directory.set(directory),
        error: (err) => console.error('getSchoolDirectory failed', err),
      });
  }

  protected photoUrl(person: SchoolPerson): string | null {
    if (!person.photo) {
      return null;
    }
    if (person.kind === 'Student' && person.studentId) {
      return this.photosApi.studentPhotoUrl(person.studentId, person.photo);
    }
    return person.userId
      ? this.photosApi.userPhotoUrl(person.userId, person.photo)
      : null;
  }

  protected kindLabel(person: SchoolPerson): string {
    switch (person.kind) {
      case 'Staff':
        return 'Zaměstnanec';
      case 'Student':
        return localizeStudentTerm('Žák', this.authApi.getSchoolType());
      default:
        return 'Rodič';
    }
  }
}
