import { Component, inject, signal } from '@angular/core';
import { Card } from '@openng/optimus-ui/card';
import {
  MyChild,
  MyChildrenApiService,
  PhotosApiService,
  SetVisibilityRequest,
  StudentPhotoState,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { NotifyService } from '../notify.service';
import { StudentPhoto } from '../student-photo/student-photo';
import { VisibilityControl } from '../visibility-control/visibility-control';
import { Loading } from '../loading/loading';
import { highlightElement, onLinkedItem } from '../highlight';

/** "Mé děti" for parents: their children, their photos, and who sees the
 * children's data — a pupil's "Viditelnost údajů" is the parents' call. */
@Component({
  selector: 'app-my-children-page',
  imports: [AppIcon, Card, Loading, StudentPhoto, VisibilityControl],
  templateUrl: './my-children-page.html',
})
export class MyChildrenPage {
  private readonly myChildrenApi = inject(MyChildrenApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  protected readonly children = signal<MyChild[] | null>(null);
  private readonly photos = signal<StudentPhotoState[]>([]);

  constructor() {
    // ?student= — a photo waiting for approval, from a notification.
    onLinkedItem('student', (id) => highlightElement(`child-${id}`));
    this.myChildrenApi.getMyChildren().subscribe({
      next: (children) => this.children.set(children),
      error: (err) => {
        console.error('getMyChildren failed', err);
        this.children.set([]);
      },
    });
    this.photosApi.getOwnStudents().subscribe({
      next: (states) => this.photos.set(states),
      error: (err) => console.error('getOwnStudents failed', err),
    });
  }

  protected photoOf(studentId: number): StudentPhotoState | undefined {
    return this.photos().find((p) => p.studentId === studentId);
  }

  protected photoChanged(state: StudentPhotoState): void {
    this.photos.update((all) =>
      all.map((p) => (p.studentId === state.studentId ? state : p)),
    );
  }

  protected setVisibility(child: MyChild, request: SetVisibilityRequest) {
    this.myChildrenApi.setVisibility(child.studentId, request).subscribe({
      next: (children) => {
        this.children.set(children);
        this.notify.success('Uloženo.');
      },
      error: (err) => this.notify.actionFailed('setVisibility failed', err),
    });
  }
}
