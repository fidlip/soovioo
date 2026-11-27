import { Injectable, inject, signal } from '@angular/core';
import type { HubConnection } from '@microsoft/signalr';
import { API_BASE_URL, AuthApiService } from '@soovioo/shared';
import { Observable, Subject, filter } from 'rxjs';

/** Waits between reconnects; the last one repeats for as long as it takes. */
const RETRY_MS = [0, 2_000, 5_000, 10_000, 30_000];

/** The tab's live connection to the API (SignalR hub `/api/live`): the
 * server says what changed, the page loads it as before. Connected while
 * someone is logged in, again whenever the token becomes someone else's
 * (another account, role, whom support mode is in as). Reconnected, it
 * reports everything as changed — something may have been missed meanwhile.
 * While it isn't connected, the pages fall back to asking now and then
 * (`connected`). In support mode the connection keeps the session alive.
 * The SignalR client loads only once someone is logged in. */
@Injectable({ providedIn: 'root' })
export class LiveService {
  private readonly authApi = inject(AuthApiService);
  private readonly baseUrl = inject(API_BASE_URL);

  private connection: HubConnection | null = null;
  private identity: string | null = null;
  private retry?: ReturnType<typeof setTimeout>;
  /** Ticket id → how many open details watch it. */
  private readonly watched = new Map<number, number>();

  private readonly countsSubject = new Subject<void>();
  private readonly ticketSubject = new Subject<number>();
  private readonly supportSubject = new Subject<void>();

  readonly connected = signal(false);
  /** The bell's, Zprávy's, the linked accounts' numbers or the support
   * banners changed. */
  readonly counts$ = this.countsSubject.asObservable();
  /** Support mode: the session changed (ended, switched whom it's in as). */
  readonly support$ = this.supportSubject.asObservable();

  constructor() {
    this.authApi.tokenChanged$.subscribe(() => this.sync());
    this.sync();
  }

  /** A watched ticket changed (its detail, thread, visits). */
  ticket$(id: number): Observable<number> {
    return this.ticketSubject.pipe(filter((changed) => changed === id));
  }

  /** While the ticket's detail is open; call the returned function on
   * closing it. */
  watchTicket(id: number): () => void {
    this.watched.set(id, (this.watched.get(id) ?? 0) + 1);
    this.invoke('WatchTicket', id);
    let done = false;
    return () => {
      if (done) {
        return;
      }
      done = true;
      const left = (this.watched.get(id) ?? 1) - 1;
      if (left > 0) {
        this.watched.set(id, left);
        return;
      }
      this.watched.delete(id);
      this.invoke('UnwatchTicket', id);
    };
  }

  /** Connects for whoever the token is now — unless it's the same as
   * before (a refreshed token). */
  private sync(): void {
    const identity = this.identityNow();
    if (identity === this.identity) {
      return;
    }
    this.identity = identity;
    this.stop();
    if (identity) {
      this.connect(identity).catch(() => undefined);
    }
  }

  private identityNow(): string | null {
    const userId = this.authApi.isLoggedIn() ? this.authApi.getUserId() : null;
    return userId === null
      ? null
      : JSON.stringify([
          userId,
          this.authApi.getRole(),
          this.authApi.getActiveRole()?.id ?? null,
          this.authApi.getSchoolName(),
          this.authApi.getSupportSessionId(),
          this.authApi.getSupportInfo()?.as ?? null,
        ]);
  }

  private async connect(identity: string): Promise<void> {
    const { HubConnectionBuilder, LogLevel } =
      await import('@microsoft/signalr');
    if (this.identity !== identity) {
      return;
    }
    const connection = new HubConnectionBuilder()
      .withUrl(`${this.baseUrl}/api/live`, {
        accessTokenFactory: async () => (await this.authApi.freshToken()) ?? '',
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (context) =>
          RETRY_MS[Math.min(context.previousRetryCount, RETRY_MS.length - 1)],
      })
      .configureLogging(LogLevel.None)
      .build();
    connection.on('counts', () => this.countsSubject.next());
    connection.on('ticket', (id: number) => this.ticketSubject.next(id));
    connection.on('support', () => this.supportSubject.next());
    connection.onreconnecting(() => this.connected.set(false));
    connection.onreconnected(() => this.caughtUp(connection));
    this.connection = connection;
    this.start(connection, 0);
  }

  /** The first connecting isn't retried by SignalR itself. */
  private start(connection: HubConnection, attempt: number): void {
    connection.start().then(
      () => this.caughtUp(connection),
      () => {
        if (this.connection === connection) {
          this.retry = setTimeout(
            () => this.start(connection, attempt + 1),
            RETRY_MS[Math.min(attempt + 1, RETRY_MS.length - 1)],
          );
        }
      },
    );
  }

  /** (Re)connected: groups are per connection — watch the tickets again —
   * and whatever happened meanwhile is reported as changed. */
  private caughtUp(connection: HubConnection): void {
    if (this.connection !== connection) {
      return;
    }
    this.connected.set(true);
    for (const id of this.watched.keys()) {
      this.invoke('WatchTicket', id);
      this.ticketSubject.next(id);
    }
    this.countsSubject.next();
    this.supportSubject.next();
  }

  private stop(): void {
    clearTimeout(this.retry);
    const connection = this.connection;
    this.connection = null;
    this.connected.set(false);
    connection?.stop().catch(() => undefined);
  }

  private invoke(method: string, id: number): void {
    if (this.connection?.state === 'Connected') {
      this.connection.invoke(method, id).catch(() => undefined);
    }
  }
}
