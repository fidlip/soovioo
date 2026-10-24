import { Component, inject, signal } from '@angular/core';
import { Card } from '@openng/optimus-ui/card';
import {
  MyChild,
  MyChildrenApiService,
  SetVisibilityRequest,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { NotifyService } from '../notify.service';
import { VisibilityControl } from '../visibility-control/visibility-control';

/** "Mé děti" for parents: their children, and who sees the children's
 * data — a pupil's "Viditelnost údajů" is the parents' call. */
@Component({
  selector: 'app-my-children-page',
  imports: [Card, AppIcon, VisibilityControl],
  templateUrl: './my-children-page.html',
})
export class MyChildrenPage {
  private readonly myChildrenApi = inject(MyChildrenApiService);
  private readonly notify = inject(NotifyService);

  protected readonly children = signal<MyChild[] | null>(null);

  constructor() {
    this.myChildrenApi.getMyChildren().subscribe({
      next: (children) => this.children.set(children),
      error: (err) => {
        console.error('getMyChildren failed', err);
        this.children.set([]);
      },
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
