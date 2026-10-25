import { Injectable, inject } from '@angular/core';
import { NavigationEnd } from '@angular/router';
import { RouterExtensions } from '@nativescript/angular';
import { Application, isAndroid, isIOS } from '@nativescript/core';
import { firebase } from '@nativescript/firebase-core';
import '@nativescript/firebase-messaging';
import { AuthApiService, NotificationsApiService } from '@soovioo/shared';
import { Subject, filter, take } from 'rxjs';

/** Pages of the app a notification can open — the others stay on the web. */
export const MOBILE_PAGES = ['grades', 'absences', 'messages'];

const NOTIFICATION_PERMISSION = 'android.permission.POST_NOTIFICATIONS';

/** Push notifications: registers this phone for the logged-in account
 * (after logging in, at start, whenever FCM changes the token), removes it
 * when logging out, and opens the page a tapped push is about. */
@Injectable({ providedIn: 'root' })
export class PushService {
  private readonly api = inject(NotificationsApiService);
  private readonly auth = inject(AuthApiService);
  private readonly router = inject(RouterExtensions);

  /** Something new may have come (a push arrived, the app came back to
   * the front) — the bar reloads its count. */
  readonly changed = new Subject<void>();

  private ready: Promise<void> | null = null;
  private token: string | null = null;

  /** Once, when the app starts. */
  start(): void {
    if (this.ready) {
      return;
    }
    Application.on(Application.resumeEvent, () => this.changed.next());
    this.ready = firebase()
      .initializeApp()
      .then(() => {
        const messaging = firebase().messaging();
        messaging.showNotificationsWhenInForeground = true;
        messaging.onToken((token) => {
          this.token = token;
          this.register();
        });
        messaging.onMessage(() => this.changed.next());
        messaging.onNotificationTap((message) => this.open(message.data));
      })
      .catch((err) => console.error('Firebase start failed', err));
    if (this.auth.isLoggedIn()) {
      this.enable();
    }
  }

  /** After logging in: asks for the permission to show notifications and
   * registers the phone. */
  async enable(): Promise<void> {
    await this.ready;
    try {
      requestNotificationPermission();
      const messaging = firebase().messaging();
      if (isIOS) {
        await messaging.requestPermission({
          ios: { alert: true, badge: true, sound: true },
        });
        await messaging.registerDeviceForRemoteMessages();
      }
      this.token = await messaging.getToken();
      this.register();
    } catch (err) {
      console.error('Push registration failed', err);
    }
  }

  /** Before logging out, while the session still lets the API know. */
  disable(): void {
    if (this.token && this.auth.isLoggedIn()) {
      this.api.removePushDevice(this.token).subscribe({
        error: (err) => console.error('removePushDevice failed', err),
      });
    }
  }

  private register(): void {
    if (!this.token || !this.auth.isLoggedIn()) {
      return;
    }
    this.api
      .registerPushDevice(this.token, isIOS ? 'Ios' : 'Android')
      .subscribe({
        error: (err) => console.error('registerPushDevice failed', err),
      });
  }

  private open(data: Record<string, string> | undefined): void {
    const page = data?.['page'] ?? '';
    const go = () => {
      this.router.navigate([
        MOBILE_PAGES.includes(page) ? `/${page}` : '/notifications',
      ]);
      this.changed.next();
    };
    // A tap that started the app comes before its first page — which
    // would replace this one.
    if (this.router.router.navigated) {
      go();
    } else {
      this.router.router.events
        .pipe(
          filter((e) => e instanceof NavigationEnd),
          take(1),
        )
        .subscribe(go);
    }
  }
}

/** Android 13+ shows no notifications until the user allows it. */
function requestNotificationPermission(): void {
  if (!isAndroid || android.os.Build.VERSION.SDK_INT < 33) {
    return;
  }
  const activity =
    Application.android.foregroundActivity ?? Application.android.startActivity;
  if (
    !activity ||
    androidx.core.content.ContextCompat.checkSelfPermission(
      activity,
      NOTIFICATION_PERMISSION,
    ) === android.content.pm.PackageManager.PERMISSION_GRANTED
  ) {
    return;
  }
  const permissions = Array.create('java.lang.String', 1);
  permissions[0] = NOTIFICATION_PERMISSION;
  androidx.core.app.ActivityCompat.requestPermissions(
    activity,
    permissions,
    1201,
  );
}
