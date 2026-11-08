import { Component, inject } from '@angular/core';
import { AuthApiService } from '@soovioo/shared';
import { SessionSyncService } from '../../session-sync.service';
import { SessionActionsService } from '../session-actions.service';
import { checkOnNavigation } from '../check-on-navigation';

/** Strips on top of every page: this tab's session changed elsewhere, or a
 * SuperAdmin is here in support mode. */
@Component({
  selector: 'app-notices',
  templateUrl: './app-notices.html',
  styleUrl: './app-notices.scss',
})
export class AppNotices {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionSync = inject(SessionSyncService);
  protected readonly sessionActions = inject(SessionActionsService);

  constructor() {
    checkOnNavigation();
  }

  protected reload(): void {
    window.location.reload();
  }
}
