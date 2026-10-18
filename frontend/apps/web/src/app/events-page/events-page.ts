import { Component, computed, inject, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  CalendarOptions,
  DateSelectArg,
  EventClickArg,
} from '@fullcalendar/core';
import csLocale from '@fullcalendar/core/locales/cs';
import dayGridPlugin from '@fullcalendar/daygrid';
import interactionPlugin from '@fullcalendar/interaction';
import listPlugin from '@fullcalendar/list';
import multiMonthPlugin from '@fullcalendar/multimonth';
import timeGridPlugin from '@fullcalendar/timegrid';
import {
  FullCalendarComponent,
  FullCalendarModule,
} from '@fullcalendar/angular';
import {
  AutoComplete,
  AutoCompleteCompleteEvent,
} from '@openng/optimus-ui/autocomplete';
import {
  Button,
  ButtonDirective,
  ButtonIcon,
  ButtonLabel,
} from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { DatePicker } from '@openng/optimus-ui/datepicker';
import { Dialog } from '@openng/optimus-ui/dialog';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Message } from '@openng/optimus-ui/message';
import { Popover } from '@openng/optimus-ui/popover';
import { Select } from '@openng/optimus-ui/select';
import { Textarea } from '@openng/optimus-ui/textarea';
import {
  AuthApiService,
  Classroom,
  ClassroomsApiService,
  CreateEventRequest,
  EventAudience,
  EventsApiService,
  PersonOption,
  RecipientOptions,
  SchoolClassOption,
  SchoolEvent,
  StudentOption,
} from '@soovioo/shared';
import { AppIcon } from '../app-icon';
import { TermPipe } from '../term.pipe';

type ViewOption = { value: string; label: string };

type AudienceOption = { value: EventAudience; label: string };

const AUDIENCE_OPTIONS: AudienceOption[] = [
  { value: 'OnlyMe', label: 'Pouze já' },
  { value: 'Everyone', label: 'Všichni' },
  { value: 'Selected', label: 'Výběr' },
];

const VIEW_OPTIONS: ViewOption[] = [
  { value: 'listWeek', label: 'List' },
  { value: 'timeGridDay', label: 'Den' },
  { value: 'timeGridWeek', label: 'Týden' },
  { value: 'dayGridMonth', label: 'Měsíc' },
  { value: 'multiMonthYear', label: 'Rok' },
];

const VIEW_STORAGE_KEY = 'soovioo-events-view';
const DEFAULT_VIEW = 'dayGridMonth';

/** Persisted choice wins (same pattern as dark mode in app.ts) — first-ever
 * visit, private browsing, or a stale/unknown value all fall back to Měsíc. */
function readInitialView(): string {
  try {
    const stored = localStorage.getItem(VIEW_STORAGE_KEY);
    if (stored && VIEW_OPTIONS.some((o) => o.value === stored)) {
      return stored;
    }
  } catch {
    // Fall through to the default below.
  }
  return DEFAULT_VIEW;
}

// Fixed palette, not a free picker — distinct enough to tell events apart
// at a glance, muted enough that FullCalendar's default white event text
// stays readable on all of them.
const COLOR_PALETTE = [
  '#6366f1', // indigo (default)
  '#ef4444', // red
  '#f97316', // orange
  '#eab308', // yellow
  '#22c55e', // green
  '#14b8a6', // teal
  '#3b82f6', // blue
  '#a855f7', // purple
  '#ec4899', // pink
  '#64748b', // slate
];
const DEFAULT_COLOR = COLOR_PALETTE[0];

const DEFAULT_DURATION_MS = 60 * 60 * 1000;
const MIN_TIMED_DURATION_MS = 15 * 60 * 1000;
const ONE_DAY_MS = 24 * 60 * 60 * 1000;
// FullCalendar's own defaults for a plain click-without-drag "selection" —
// one day in dayGridMonth, one slotDuration (its default is 30 min) in
// timeGrid. Used to tell "user just clicked a spot" apart from "user
// deliberately dragged a range" so a bare click still gets our own default
// duration instead of a trivial 30-minute sliver.
const MINIMAL_ALLDAY_SELECTION_MS = ONE_DAY_MS;
const MINIMAL_TIMED_SELECTION_MS = 30 * 60 * 1000;

function startOfDay(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), d.getDate());
}

@Component({
  selector: 'app-events-page',
  imports: [
    TermPipe,
    AppIcon,
    AutoComplete,
    FormsModule,
    FullCalendarModule,
    Button,
    ButtonDirective,
    ButtonIcon,
    ButtonLabel,
    Card,
    DatePicker,
    Dialog,
    InputText,
    Message,
    Popover,
    Select,
    Textarea,
  ],
  templateUrl: './events-page.html',
})
export class EventsPage {
  private readonly eventsApi = inject(EventsApiService);
  private readonly classroomsApi = inject(ClassroomsApiService);
  private readonly authApi = inject(AuthApiService);

  private readonly calendar = viewChild<FullCalendarComponent>('calendar');

  protected readonly viewOptions = VIEW_OPTIONS;
  protected readonly audienceOptions = AUDIENCE_OPTIONS;
  protected readonly colorPalette = COLOR_PALETTE;
  protected readonly activeView = signal(readInitialView());
  protected readonly isAdmin = this.authApi.isAdmin();

  protected readonly events = signal<SchoolEvent[]>([]);
  protected readonly feedUrl = signal<string | null>(null);
  protected readonly feedUrlCopied = signal(false);
  protected readonly formError = signal<string | null>(null);
  protected readonly dialogVisible = signal(false);

  protected readonly webcalUrl = computed(
    () => this.feedUrl()?.replace(/^https?:/, 'webcal:') ?? null,
  );

  protected readonly calendarOptions = computed<CalendarOptions>(() => ({
    plugins: [
      dayGridPlugin,
      timeGridPlugin,
      listPlugin,
      multiMonthPlugin,
      interactionPlugin,
    ],
    initialView: this.activeView(),
    locale: csLocale,
    headerToolbar: false,
    height: 'auto',
    // Den/Týden only — Month/List/Year ignore this. Bounds the visible
    // slots to roughly the school day (periods run 8:00–13:30, see
    // SchoolPeriod seed data) plus a margin for after-school activities, so
    // scrolling through the small hours isn't the default. An event outside
    // this window (e.g. a late-evening meeting) just won't show in Day/Week.
    slotMinTime: '07:00:00',
    slotMaxTime: '18:00:00',
    selectable: this.canManageEvents(),
    events: this.events().map((e) => ({
      id: e.id.toString(),
      title: e.title,
      start: e.startAt,
      end: e.endAt,
      allDay: e.allDay,
      backgroundColor: e.color,
      borderColor: e.color,
    })),
    // `select` alone (not `dateClick`) covers both "clicked one empty spot"
    // and "dragged a range" — FullCalendar fires it for either when
    // selectable is on, with a real start/end either way.
    select: (arg: DateSelectArg) => this.onSelect(arg),
    eventClick: (arg: EventClickArg) => this.onEventClick(arg),
  }));

  // Written both from template ngModel bindings and from FullCalendar's own
  // click handling (select/eventClick) — the latter runs outside any
  // Angular-dispatched event, so this app's zoneless setup (no zone.js
  // polyfill; see ROADMAP.md "Editace a mazání") only picks up the change
  // reliably if it's a signal, not a plain field.
  protected readonly editingEventId = signal<number | null>(null);
  protected readonly title = signal('');
  protected readonly description = signal('');
  protected readonly location = signal('');
  /** Quick-fill suggestions for the free-text location field, sourced from
   * existing classrooms — no new FK on Event, see ROADMAP "Správa učeben". */
  protected readonly classrooms = signal<Classroom[]>([]);
  protected readonly locationSuggestions = signal<string[]>([]);
  protected readonly color = signal(DEFAULT_COLOR);
  protected readonly allDay = signal(false);
  protected readonly startAt = signal(new Date());
  protected readonly endAt = signal(new Date(Date.now() + DEFAULT_DURATION_MS));

  protected readonly audience = signal<EventAudience>('OnlyMe');
  protected readonly selectedClassIds = signal<number[]>([]);
  protected readonly selectedTeacherIds = signal<number[]>([]);
  protected readonly selectedParentIds = signal<number[]>([]);
  protected readonly selectedStudentIds = signal<number[]>([]);
  protected readonly recipientOptions = signal<RecipientOptions | null>(null);

  // Plain fields, not signals — read directly from the template via a
  // method call, same filter pattern as admin-page.ts (ngModelChange's own
  // event dispatch triggers change detection on this zoneless app regardless).
  protected classFilterName = '';
  protected classFilterRocnik: number | null = null;
  protected studentFilterName = '';
  protected studentFilterRocnik: number | null = null;
  protected teacherFilterName = '';
  protected parentFilterName = '';

  /** Od→Do gap the form currently remembers — reapplied whenever Od changes
   * so Do tracks it, and updated whenever Do is edited directly. Plain
   * field, not a signal: it's never read from the template, only used to
   * compute the signal values above. */
  private durationMs = DEFAULT_DURATION_MS;

  constructor() {
    this.reload();
    this.eventsApi.getCalendarFeedUrl().subscribe({
      next: (result) => this.feedUrl.set(result.feedUrl),
      error: (err) => console.error('getCalendarFeedUrl failed', err),
    });
    if (this.canManageEvents()) {
      this.eventsApi.getRecipientOptions().subscribe({
        next: (options) => this.recipientOptions.set(options),
        error: (err) => console.error('getRecipientOptions failed', err),
      });
      this.classroomsApi.getClassrooms().subscribe({
        next: (classrooms) => this.classrooms.set(classrooms),
        error: (err) => console.error('getClassrooms failed', err),
      });
    }
  }

  protected canManageEvents(): boolean {
    return this.authApi.isTeacherOrAdmin();
  }

  /** p-autoComplete's completeMethod — filters the already-loaded classroom
   * list client-side (small, fixed set per school, no need for a backend
   * round-trip like searchMunicipalities does for the much larger RAOR
   * table). Free text typed by the user is still accepted either way. */
  protected searchLocations(event: AutoCompleteCompleteEvent): void {
    const query = event.query.trim().toLowerCase();
    this.locationSuggestions.set(
      this.classrooms()
        .map((c) => c.name)
        .filter((name) => name.toLowerCase().includes(query)),
    );
  }

  protected changeView(view: string): void {
    this.activeView.set(view);
    this.calendar()?.getApi().changeView(view);
    try {
      localStorage.setItem(VIEW_STORAGE_KEY, view);
    } catch {
      // Private browsing / blocked storage — the switch still works for
      // this page load, it just won't survive a reload.
    }
  }

  protected copyFeedUrl(): void {
    const url = this.feedUrl();
    if (!url) {
      return;
    }
    navigator.clipboard.writeText(url).then(() => {
      this.feedUrlCopied.set(true);
      setTimeout(() => this.feedUrlCopied.set(false), 2000);
    });
  }

  /** The "+" button — no calendar context, so Od defaults to now. */
  protected openCreateDialog(): void {
    this.resetForm();
    this.dialogVisible.set(true);
  }

  protected onStartChanged(value: Date): void {
    this.startAt.set(value);
    const start = this.normalized(value);
    this.endAt.set(new Date(start.getTime() + this.durationMs));
  }

  protected onEndChanged(value: Date): void {
    this.endAt.set(value);
    const start = this.normalized(this.startAt());
    const end = this.normalized(value);
    const minMs = this.allDay() ? 0 : MIN_TIMED_DURATION_MS;
    this.durationMs = Math.max(minMs, end.getTime() - start.getTime());
  }

  protected onAllDayToggle(value: boolean): void {
    this.allDay.set(value);
    if (value) {
      // Switch to whole-day granularity for the currently remembered span.
      this.durationMs = Math.round(this.durationMs / ONE_DAY_MS) * ONE_DAY_MS;
    } else if (this.durationMs < MIN_TIMED_DURATION_MS) {
      this.durationMs = DEFAULT_DURATION_MS;
    }
    const start = this.normalized(this.startAt());
    this.endAt.set(new Date(start.getTime() + this.durationMs));
  }

  protected filteredClasses(): SchoolClassOption[] {
    const name = this.classFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.classes ?? []).filter(
      (c) =>
        (!name || c.name.toLowerCase().includes(name)) &&
        (this.classFilterRocnik === null ||
          c.rocnik === this.classFilterRocnik),
    );
  }

  protected filteredStudents(): StudentOption[] {
    const name = this.studentFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.students ?? []).filter(
      (s) =>
        (!name ||
          `${s.firstName} ${s.lastName}`.toLowerCase().includes(name)) &&
        (this.studentFilterRocnik === null ||
          s.rocnik === this.studentFilterRocnik),
    );
  }

  protected filteredTeachers(): PersonOption[] {
    const name = this.teacherFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.teachers ?? []).filter(
      (t) => !name || t.username.toLowerCase().includes(name),
    );
  }

  protected filteredParents(): PersonOption[] {
    const name = this.parentFilterName.trim().toLowerCase();
    return (this.recipientOptions()?.parents ?? []).filter(
      (p) => !name || p.username.toLowerCase().includes(name),
    );
  }

  /** Every ročník that actually occurs among the caller's pickable classes
   * — drives the ročník filter dropdown's options, so it never offers a
   * grade that would just empty the list. */
  protected rocnikOptions(): number[] {
    const classes = this.recipientOptions()?.classes ?? [];
    const rocniky = new Set(
      classes.map((c) => c.rocnik).filter((r): r is number => r != null),
    );
    return [...rocniky].sort((a, b) => a - b);
  }

  protected toggleClassId(id: number): void {
    this.selectedClassIds.update((ids) =>
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
  }

  protected toggleTeacherId(id: number): void {
    this.selectedTeacherIds.update((ids) =>
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
  }

  protected toggleParentId(id: number): void {
    this.selectedParentIds.update((ids) =>
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
  }

  protected toggleStudentId(id: number): void {
    this.selectedStudentIds.update((ids) =>
      ids.includes(id) ? ids.filter((x) => x !== id) : [...ids, id],
    );
  }

  protected onDialogVisibleChange(visible: boolean): void {
    this.dialogVisible.set(visible);
    if (!visible) {
      this.resetForm();
    }
  }

  protected save(): void {
    if (!this.title()) {
      return;
    }
    this.formError.set(null);

    const start = this.normalized(this.startAt());
    const endDisplay = this.normalized(this.endAt());
    // All-day end is exclusive in the data model (iCal/FullCalendar
    // convention) but the form shows the last included day — so +1 day only
    // happens here, at the model boundary.
    const end = this.allDay()
      ? new Date(endDisplay.getTime() + ONE_DAY_MS)
      : endDisplay;

    const request: CreateEventRequest = {
      title: this.title(),
      description: this.description() || undefined,
      location: this.location() || undefined,
      allDay: this.allDay(),
      startAt: start.toISOString(),
      endAt: end.toISOString(),
      color: this.color(),
      audience: this.audience(),
      schoolClassIds: this.selectedClassIds(),
      teacherUserIds: this.selectedTeacherIds(),
      parentUserIds: this.selectedParentIds(),
      studentIds: this.selectedStudentIds(),
    };

    const editingId = this.editingEventId();
    const result$ = editingId
      ? this.eventsApi.updateEvent(editingId, request)
      : this.eventsApi.createEvent(request);

    result$.subscribe({
      next: () => {
        this.dialogVisible.set(false);
        this.resetForm();
        this.reload();
      },
      error: () => this.formError.set('Uložení se nezdařilo.'),
    });
  }

  protected deleteEvent(): void {
    const editingId = this.editingEventId();
    if (!editingId) {
      return;
    }
    this.eventsApi.deleteEvent(editingId).subscribe({
      next: () => {
        this.dialogVisible.set(false);
        this.resetForm();
        this.reload();
      },
      error: (err) => console.error('deleteEvent failed', err),
    });
  }

  protected cancelEdit(): void {
    this.dialogVisible.set(false);
    this.resetForm();
  }

  private onSelect(arg: DateSelectArg): void {
    if (!this.canManageEvents()) {
      this.calendar()?.getApi().unselect();
      return;
    }
    this.resetForm();

    const minimalMs = arg.allDay
      ? MINIMAL_ALLDAY_SELECTION_MS
      : MINIMAL_TIMED_SELECTION_MS;
    const isMinimalClick = arg.end.getTime() - arg.start.getTime() <= minimalMs;

    this.allDay.set(arg.allDay);
    this.startAt.set(arg.start);

    if (isMinimalClick) {
      // A plain click, not a deliberate drag — use our own default span
      // instead of FullCalendar's trivial one-unit selection.
      this.endAt.set(new Date(arg.start.getTime() + this.durationMs));
    } else {
      const displayEnd = arg.allDay
        ? new Date(arg.end.getTime() - ONE_DAY_MS)
        : arg.end;
      this.endAt.set(displayEnd);
      this.durationMs = Math.max(0, displayEnd.getTime() - arg.start.getTime());
    }

    this.calendar()?.getApi().unselect();
    this.dialogVisible.set(true);
  }

  private onEventClick(arg: EventClickArg): void {
    if (!this.canManageEvents()) {
      return;
    }
    const event = this.events().find((e) => e.id.toString() === arg.event.id);
    if (!event) {
      return;
    }

    const start = new Date(event.startAt);
    const endRaw = new Date(event.endAt);
    const end = event.allDay ? new Date(endRaw.getTime() - ONE_DAY_MS) : endRaw;

    this.editingEventId.set(event.id);
    this.title.set(event.title);
    this.description.set(event.description ?? '');
    this.location.set(event.location ?? '');
    this.color.set(event.color ?? DEFAULT_COLOR);
    this.allDay.set(event.allDay);
    this.startAt.set(start);
    this.endAt.set(end);
    this.durationMs = Math.max(0, end.getTime() - start.getTime());
    this.audience.set(event.audience);
    this.selectedClassIds.set(event.schoolClassIds);
    this.selectedTeacherIds.set(event.teacherUserIds);
    this.selectedParentIds.set(event.parentUserIds);
    this.selectedStudentIds.set(event.studentIds);
    this.dialogVisible.set(true);
  }

  /** All-day ignores the time-of-day component entirely (always midnight),
   * so duration math and the saved payload stay in whole days regardless of
   * whatever time-of-day the underlying Date happens to carry. */
  private normalized(d: Date): Date {
    return this.allDay() ? startOfDay(d) : d;
  }

  private resetForm(): void {
    this.editingEventId.set(null);
    this.title.set('');
    this.description.set('');
    this.location.set('');
    this.color.set(DEFAULT_COLOR);
    this.allDay.set(false);
    this.durationMs = DEFAULT_DURATION_MS;
    const now = new Date();
    this.startAt.set(now);
    this.endAt.set(new Date(now.getTime() + DEFAULT_DURATION_MS));
    this.audience.set('OnlyMe');
    this.selectedClassIds.set([]);
    this.selectedTeacherIds.set([]);
    this.selectedParentIds.set([]);
    this.selectedStudentIds.set([]);
    this.classFilterName = '';
    this.classFilterRocnik = null;
    this.studentFilterName = '';
    this.studentFilterRocnik = null;
    this.teacherFilterName = '';
    this.parentFilterName = '';
  }

  private reload(): void {
    this.eventsApi.getEvents().subscribe({
      next: (events) => this.events.set(events),
      error: (err) => console.error('getEvents failed', err),
    });
  }
}
