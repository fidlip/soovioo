import { Component, computed, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Tag } from '@openng/optimus-ui/tag';
import {
  DateFormatPipe,
  PhotosApiService,
  StudentPhotoState,
} from '@soovioo/shared';
import { Observable } from 'rxjs';
import { NotifyService } from '../notify.service';
import { PhotoEditor } from '../photo-editor/photo-editor';
import { TermPipe } from '../term.pipe';
import { FloatField } from '../forms';

/** A pupil's photo with the guardian's consent around it — the same block
 * for a parent (Mé děti), the pupil (Nastavení) and the school
 * (Administrace); what each may do comes from the API (`can…` flags). */
@Component({
  selector: 'app-student-photo',
  imports: [
    FloatField,
    DateFormatPipe,
    FormsModule,
    Button,
    InputText,
    Tag,
    PhotoEditor,
    TermPipe,
  ],
  templateUrl: './student-photo.html',
})
export class StudentPhoto {
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  readonly state = input.required<StudentPhotoState>();
  readonly changed = output<StudentPhotoState>();

  protected consentGivenBy = '';

  protected readonly photoUrl = computed(() => {
    const s = this.state();
    return s.photo
      ? this.photosApi.studentPhotoUrl(s.studentId, s.photo, 'profile')
      : null;
  });

  protected upload(file: File): void {
    this.run(
      this.photosApi.uploadStudentPhoto(
        this.state().studentId,
        file,
        file.name,
      ),
      'Fotka nahrána.',
    );
  }

  protected remove(): void {
    this.run(
      this.photosApi.deleteStudentPhoto(this.state().studentId),
      'Fotka smazána.',
    );
  }

  protected approve(): void {
    this.run(
      this.photosApi.approveStudentPhoto(this.state().studentId),
      'Fotka schválena.',
    );
  }

  protected reject(): void {
    this.run(
      this.photosApi.rejectStudentPhoto(this.state().studentId),
      'Fotka zamítnuta a smazána.',
    );
  }

  protected giveConsent(): void {
    const givenBy = this.state().consentNeedsName
      ? this.consentGivenBy.trim()
      : undefined;
    this.run(
      this.photosApi.giveConsent(this.state().studentId, givenBy),
      'Souhlas zaznamenán.',
    );
    this.consentGivenBy = '';
  }

  protected revokeConsent(): void {
    this.run(
      this.photosApi.revokeConsent(this.state().studentId),
      'Souhlas odvolán, fotka smazána.',
    );
  }

  private run(request: Observable<StudentPhotoState>, success: string): void {
    request.subscribe({
      next: (state) => {
        this.changed.emit(state);
        this.notify.success(success);
      },
      error: (err) =>
        this.notify.actionFailed('student photo action failed', err),
    });
  }
}
