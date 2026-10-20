import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, inject, signal } from '@angular/core';

const FIRST_PROMPT_SECONDS = 10 * 60;
const NEXT_PROMPT_SECONDS = 60 * 60;
const TICK_SECONDS = 15;
/** Counts as "using the app" only this long after the last input. */
const IDLE_AFTER_MS = 2 * 60 * 1000;

const STORE_KEY = 'soovioo-demo-feedback';
const SESSION_KEY = 'soovioo-demo-feedback-exit-asked';

interface Stored {
  activeSeconds: number;
  /** activeSeconds when the dialog last opened (0 = never). */
  lastPromptAt: number;
  /** "Už se neptat" or sent — no more automatic prompts. */
  muted: boolean;
}

/** When the demo asks for feedback: after 10 minutes of actual use (the
 * tab visible and someone clicking or typing), then every hour, and when
 * the mouse leaves the page towards the tabs or the close button (desktop,
 * once per visit). `beforeunload` can't show our own dialog, so that's
 * the closest a page gets to "on leaving". */
@Injectable({ providedIn: 'root' })
export class DemoFeedbackService {
  private readonly document = inject(DOCUMENT);
  readonly open = signal(false);
  private started = false;
  private lastInput = Date.now();

  start(destroyRef: DestroyRef): void {
    if (this.started) {
      return;
    }
    this.started = true;
    const window = this.document.defaultView!;
    const onInput = () => (this.lastInput = Date.now());
    const onMouseOut = (e: MouseEvent) => {
      if (!e.relatedTarget && e.clientY <= 0) {
        this.exitIntent();
      }
    };
    for (const type of ['pointerdown', 'keydown', 'scroll', 'mousemove']) {
      window.addEventListener(type, onInput, { passive: true });
    }
    this.document.addEventListener('mouseout', onMouseOut);
    const timer = window.setInterval(() => this.tick(), TICK_SECONDS * 1000);
    destroyRef.onDestroy(() => {
      window.clearInterval(timer);
      for (const type of ['pointerdown', 'keydown', 'scroll', 'mousemove']) {
        window.removeEventListener(type, onInput);
      }
      this.document.removeEventListener('mouseout', onMouseOut);
      this.started = false;
    });
  }

  /** Opened by hand ("Zpětná vazba" in the demo bar). */
  show(): void {
    this.open.set(true);
  }

  /** Sent, or "Už se neptat". */
  mute(): void {
    this.write({ ...this.read(), muted: true });
    this.open.set(false);
  }

  private tick(): void {
    if (this.document.visibilityState !== 'visible' || Date.now() - this.lastInput > IDLE_AFTER_MS) {
      return;
    }
    const state = this.read();
    state.activeSeconds += TICK_SECONDS;
    const due = state.lastPromptAt === 0
      ? state.activeSeconds >= FIRST_PROMPT_SECONDS
      : state.activeSeconds - state.lastPromptAt >= NEXT_PROMPT_SECONDS;
    if (due && !state.muted && !this.open()) {
      state.lastPromptAt = state.activeSeconds;
      this.open.set(true);
    }
    this.write(state);
  }

  private exitIntent(): void {
    const state = this.read();
    // Not before the visitor has seen anything, nor twice per visit.
    if (state.muted || this.open() || state.activeSeconds < 60 || sessionFlag()) {
      return;
    }
    setSessionFlag();
    this.open.set(true);
  }

  private read(): Stored {
    try {
      const raw = localStorage.getItem(STORE_KEY);
      if (raw) {
        return { activeSeconds: 0, lastPromptAt: 0, muted: false, ...JSON.parse(raw) };
      }
    } catch {
      // Blocked or garbled storage — start over.
    }
    return { activeSeconds: 0, lastPromptAt: 0, muted: false };
  }

  private write(state: Stored): void {
    try {
      localStorage.setItem(STORE_KEY, JSON.stringify(state));
    } catch {
      // Blocked storage — the counter just restarts with the next visit.
    }
  }
}

function sessionFlag(): boolean {
  try {
    return sessionStorage.getItem(SESSION_KEY) === '1';
  } catch {
    return false;
  }
}

function setSessionFlag(): void {
  try {
    sessionStorage.setItem(SESSION_KEY, '1');
  } catch {
    // Blocked storage — may ask once more this visit.
  }
}
