import {
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { Dialog } from '@openng/optimus-ui/dialog';
import { PhotosApiService, StudentPhotoState } from '@soovioo/shared';
import { NotifyService } from '../notify.service';
import { PhotoEditor } from '../photo-editor/photo-editor';
import { StudentPhoto } from '../student-photo/student-photo';

/** Administrace: a pupil's photo and consent (admin, class teacher).
 * Open while `studentId` is set. */
@Component({
  selector: 'app-student-photo-dialog',
  imports: [Dialog, StudentPhoto],
  template: `
    <p-dialog
      [visible]="studentId() !== null"
      (visibleChange)="!$event && closed.emit()"
      [header]="state() ? 'Fotka — ' + state()!.name : 'Fotka'"
      [modal]="true"
      styleClass="w-full max-w-lg"
    >
      @if (state(); as s) {
        <app-student-photo [state]="s" (changed)="update($event)" />
      } @else {
        <p class="m-0 text-surface-500">Načítání…</p>
      }
    </p-dialog>
  `,
})
export class StudentPhotoDialog {
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  readonly studentId = input<number | null>(null);
  readonly closed = output<void>();
  /** The photo token changed (new, approved or gone). */
  readonly photoChanged = output<{ studentId: number; photo: string | null }>();

  protected readonly state = signal<StudentPhotoState | null>(null);

  constructor() {
    effect(() => {
      const id = this.studentId();
      this.state.set(null);
      if (id === null) {
        return;
      }
      this.photosApi.getStudentState(id).subscribe({
        next: (state) => this.state.set(state),
        error: (err) => this.notify.actionFailed('getStudentState failed', err),
      });
    });
  }

  protected update(state: StudentPhotoState): void {
    this.state.set(state);
    this.photoChanged.emit({ studentId: state.studentId, photo: state.photo });
  }
}

/** Administrace: an admin sets or removes a staff member's photo. Open
 * while `user` is set. */
@Component({
  selector: 'app-user-photo-dialog',
  imports: [Dialog, PhotoEditor],
  template: `
    <p-dialog
      [visible]="user() !== null"
      (visibleChange)="!$event && closed.emit()"
      [header]="'Fotka — ' + (user()?.name ?? '')"
      [modal]="true"
      styleClass="w-full max-w-md"
    >
      @if (user(); as u) {
        <app-photo-editor
          [name]="u.name"
          [photoUrl]="photoUrl()"
          (upload)="upload(u.id, $event)"
          (remove)="remove(u.id)"
        />
      }
    </p-dialog>
  `,
})
export class UserPhotoDialog {
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  readonly user = input<{
    id: number;
    name: string;
    photo?: string | null;
  } | null>(null);
  readonly closed = output<void>();
  readonly photoChanged = output<{ userId: number; photo: string | null }>();

  /** Follows the input, then whatever this dialog uploaded. */
  private readonly photo = signal<string | null>(null);
  protected readonly photoUrl = computed(() => {
    const u = this.user();
    const photo = this.photo();
    return u && photo
      ? this.photosApi.userPhotoUrl(u.id, photo, 'profile')
      : null;
  });

  constructor() {
    effect(() => this.photo.set(this.user()?.photo ?? null));
  }

  protected upload(userId: number, file: File): void {
    this.photosApi.uploadUserPhoto(userId, file, file.name).subscribe({
      next: ({ photo }) => {
        this.photo.set(photo);
        this.photoChanged.emit({ userId, photo });
        this.notify.success('Fotka nahrána.');
      },
      error: (err) => this.notify.actionFailed('uploadUserPhoto failed', err),
    });
  }

  protected remove(userId: number): void {
    this.photosApi.deleteUserPhoto(userId).subscribe({
      next: () => {
        this.photo.set(null);
        this.photoChanged.emit({ userId, photo: null });
        this.notify.success('Fotka smazána.');
      },
      error: (err) => this.notify.actionFailed('deleteUserPhoto failed', err),
    });
  }
}
