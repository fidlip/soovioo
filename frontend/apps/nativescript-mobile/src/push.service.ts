import { Injectable, inject } from '@angular/core';
import { NavigationEnd } from '@angular/router';
import { RouterExtensions } from '@nativescript/angular';
import {
  AndroidActivityNewIntentEventData,
  AndroidApplication,
  Application,
  Utils,
  isAndroid,
  isIOS,
} from '@nativescript/core';
import { firebase } from '@nativescript/firebase-core';
import '@nativescript/firebase-messaging';
import type { RemoteMessage } from '@nativescript/firebase-messaging';
import { AuthApiService, NotificationsApiService } from '@soovioo/shared';
import { Subject, filter, take } from 'rxjs';

/** Pages of the app a notification can open — the others stay on the web. */
export const MOBILE_PAGES = ['grades', 'absences', 'messages'];

const NOTIFICATION_PERMISSION = 'android.permission.POST_NOTIFICATIONS';

/** The Android channel of all our notifications, also FCM's default for
 * the ones it shows itself (AndroidManifest.xml). */
const CHANNEL_ID = 'upozorneni';

/** The page a notification shown by the app itself opens. */
const PAGE_EXTRA = 'cz.soovioo.app.page';

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
    if (isAndroid) {
      createChannel();
      Application.android.on(
        AndroidApplication.activityNewIntentEvent,
        (args: AndroidActivityNewIntentEventData) => {
          const page = args.intent?.getStringExtra(PAGE_EXTRA);
          if (page) {
            this.open({ page });
          }
        },
      );
    }
    this.ready = firebase()
      .initializeApp()
      .then(() => {
        const messaging = firebase().messaging();
        messaging.showNotificationsWhenInForeground = true;
        messaging.onToken((token) => {
          this.token = token;
          this.register();
        });
        messaging.onMessage((message) => {
          // Android shows nothing on its own while the app is open.
          if (isAndroid) {
            showAndroidNotification(message);
          }
          this.changed.next();
        });
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

function createChannel(): void {
  if (android.os.Build.VERSION.SDK_INT < 26) {
    return;
  }
  const context = Utils.android.getApplicationContext();
  const manager = context.getSystemService(
    android.content.Context.NOTIFICATION_SERVICE,
  ) as android.app.NotificationManager;
  manager.createNotificationChannel(
    new android.app.NotificationChannel(
      CHANNEL_ID,
      'Upozornění',
      android.app.NotificationManager.IMPORTANCE_HIGH,
    ),
  );
}

/** A push that came while the app is open, as a notification of its own —
 * tapping it brings the app's activity a new intent with the page. */
function showAndroidNotification(message: RemoteMessage): void {
  const context = Utils.android.getApplicationContext();
  const intent = context
    .getPackageManager()
    .getLaunchIntentForPackage(context.getPackageName());
  if (!intent) {
    return;
  }
  intent.putExtra(PAGE_EXTRA, message.data?.['page'] ?? '');
  intent.addFlags(android.content.Intent.FLAG_ACTIVITY_SINGLE_TOP);
  const id = Date.now() % 2147483647;
  const tap = android.app.PendingIntent.getActivity(
    context,
    id,
    intent,
    android.app.PendingIntent.FLAG_IMMUTABLE |
      android.app.PendingIntent.FLAG_UPDATE_CURRENT,
  );
  const notification = new androidx.core.app.NotificationCompat.Builder(
    context,
    CHANNEL_ID,
  )
    .setSmallIcon(context.getApplicationInfo().icon)
    .setContentTitle(message.notification?.title ?? 'Soovioo')
    .setContentText(message.notification?.body ?? '')
    .setAutoCancel(true)
    .setContentIntent(tap)
    .build();
  androidx.core.app.NotificationManagerCompat.from(context).notify(
    id,
    notification,
  );
}
