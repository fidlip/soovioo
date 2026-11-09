import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import {
  LINKED_ITEM_GONE,
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
import { FloatField } from '../forms/float-field';

/** "Mé děti" for parents: their children, their photos, and who sees the
 * children's data — a pupil's "Viditelnost údajů" is the parents' call. */
@Component({
  selector: 'app-my-children-page',
  imports: [
    AppIcon,
    Button,
    Card,
    FloatField,
    FormsModule,
    InputText,
    Loading,
    StudentPhoto,
    VisibilityControl,
  ],
  templateUrl: './my-children-page.html',
})
export class MyChildrenPage {
  private readonly myChildrenApi = inject(MyChildrenApiService);
  private readonly photosApi = inject(PhotosApiService);
  private readonly notify = inject(NotifyService);

  protected readonly children = signal<MyChild[] | null>(null);
  private readonly photos = signal<StudentPhotoState[]>([]);
  private linkedId: number | null = null;
  /** Nicknames being typed, per child, until saved. */
  protected readonly drafts = new Map<number, string>();

  constructor() {
    // ?student= — a photo waiting for approval, from a notification.
    onLinkedItem('student', (id) => {
      this.linkedId = id;
      this.showLinked();
    });
    this.myChildrenApi.getMyChildren().subscribe({
      next: (children) => {
        this.children.set(children);
        this.showLinked();
      },
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

  private showLinked(): void {
    const children = this.children();
    const id = this.linkedId;
    if (!children || !id) {
      return;
    }
    this.linkedId = null;
    if (children.some((c) => c.studentId === id)) {
      highlightElement(`child-${id}`);
    } else {
      this.notify.warn(LINKED_ITEM_GONE);
    }
  }

  protected photoOf(studentId: number): StudentPhotoState | undefined {
    return this.photos().find((p) => p.studentId === studentId);
  }

  protected photoChanged(state: StudentPhotoState): void {
    this.photos.update((all) =>
      all.map((p) => (p.studentId === state.studentId ? state : p)),
    );
  }

  protected nicknameDraft(child: MyChild): string {
    return this.drafts.get(child.studentId) ?? child.nickname ?? '';
  }

  protected saveNickname(child: MyChild): void {
    this.myChildrenApi
      .setNickname(child.studentId, this.nicknameDraft(child))
      .subscribe({
        next: (children) => {
          this.drafts.delete(child.studentId);
          this.children.set(children);
          this.notify.success('Uloženo.');
        },
        error: (err) => this.notify.actionFailed('setNickname failed', err),
      });
  }

  protected decideNickname(child: MyChild, approve: boolean): void {
    this.myChildrenApi.decideNickname(child.studentId, approve).subscribe({
      next: (children) => {
        this.drafts.delete(child.studentId);
        this.children.set(children);
        this.notify.success(approve ? 'Přezdívka schválena.' : 'Zamítnuto.');
      },
      error: (err) => this.notify.actionFailed('decideNickname failed', err),
    });
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
