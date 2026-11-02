import { DestroyRef, Injectable, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AuthApiService, SessionChange } from '@soovioo/shared';
import { currentSchoolSlugFromUrl } from './school-context';

const CHANNEL = 'soovioo-session';

type Announcement = SessionChange & { schoolSlug: string | null };

/** Tabs of one school share its refresh cookie: a login, logout or role
 * switch in one of them is what the others continue with on their next
 * refresh. Instead of switching silently, they say so and offer a reload. */
@Injectable({ providedIn: 'root' })
export class SessionSyncService {
  private readonly authApi = inject(AuthApiService);
  private readonly channel =
    typeof BroadcastChannel === 'undefined'
      ? null
      : new BroadcastChannel(CHANNEL);

  /** What changed in another tab, null while nothing did. */
  readonly notice = signal<string | null>(null);

  constructor() {
    const channel = this.channel;
    if (!channel) {
      return;
    }
    this.authApi.sessionChanged$
      .pipe(takeUntilDestroyed())
      .subscribe((change) =>
        channel.postMessage({
          ...change,
          schoolSlug: currentSchoolSlugFromUrl(),
        } satisfies Announcement),
      );
    const onMessage = (event: MessageEvent<Announcement>) =>
      this.received(event.data);
    channel.addEventListener('message', onMessage);
    inject(DestroyRef).onDestroy(() => {
      channel.removeEventListener('message', onMessage);
      channel.close();
    });
  }

  private received(change: Announcement): void {
    if (
      change.schoolSlug !== currentSchoolSlugFromUrl() ||
      !this.authApi.isLoggedIn()
    ) {
      return;
    }
    switch (change.kind) {
      case 'login':
        if (change.username !== this.authApi.getUsername()) {
          this.notice.set(
            `V jiné záložce jste se přihlásili jako ${change.username}.`,
          );
        }
        break;
      case 'logout':
        this.notice.set('V jiné záložce jste se odhlásili.');
        break;
      case 'role':
        if (change.roleName === this.authApi.getActiveRole()?.name) {
          break;
        }
        this.notice.set(
          `V jiné záložce jste přepnuli roli na ${change.roleName}.`,
        );
        break;
    }
  }
}
