import { Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Button, ButtonDirective } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AbsenceStatus,
  AbsencesApiService,
  AuthApiService,
  BehaviorApiService,
  BehaviorRecordType,
  Dashboard,
  DashboardApiService,
} from '@soovioo/shared';
import { SchoolContextService } from '../school-context';
import { AppIcon, AppIconName } from '../app-icon';

const ABSENCE_STATUS_SEVERITY: Record<
  AbsenceStatus,
  'success' | 'warn' | 'danger' | 'secondary'
> = {
  Unexcused: 'secondary',
  PendingExcuse: 'warn',
  Excused: 'success',
  Rejected: 'danger',
};

const ABSENCE_STATUS_LABEL: Record<AbsenceStatus, string> = {
  Unexcused: 'Neomluveno',
  PendingExcuse: 'Čeká na schválení',
  Excused: 'Omluveno',
  Rejected: 'Zamítnuto',
};

/** Same labels/severities as behavior-page.ts's RECORD_TYPE_* — small
 * enough that duplicating them here beats importing across pages. */
const RECORD_TYPE_LABELS: Record<BehaviorRecordType, string> = {
  Pochvala: 'Pochvala',
  Poznamka: 'Poznámka',
  NapomenutiTridnihoUcitele: 'Napomenutí třídního učitele',
  DutkaTridnihoUcitele: 'Důtka třídního učitele',
  NapomenutiReditele: 'Napomenutí ředitele školy',
  DutkaReditele: 'Důtka ředitele školy',
};

const RECORD_TYPE_SEVERITY: Record<
  BehaviorRecordType,
  'success' | 'info' | 'warn' | 'danger'
> = {
  Pochvala: 'success',
  Poznamka: 'info',
  NapomenutiTridnihoUcitele: 'warn',
  DutkaTridnihoUcitele: 'danger',
  NapomenutiReditele: 'warn',
  DutkaReditele: 'danger',
};

type WidgetId =
  | 'school'
  | 'timetable'
  | 'grades'
  | 'absences'
  | 'messages'
  | 'behavior'
  | 'events';

interface Widget {
  id: WidgetId;
  title: string;
  icon: AppIconName;
  /** Agenda path (see SchoolContextService.link). */
  link: string;
  linkLabel: string;
  /** False = nothing new/pending — the widget collapses into a chip. */
  hasContent: boolean;
}

/** Role-specific widget set, per the roadmap's "Home page (dashboard)" spec:
 * Parent/Student get their own schedule/grades/absences; Teacher/Admin get
 * the operational view (today's lessons taught, absences awaiting review);
 * Admin additionally gets school-wide counts. Every widget here mirrors
 * what the equivalent full agenda page would show — see DashboardController
 * for the shared access-control reasoning. */
@Component({
  selector: 'app-home-page',
  imports: [
    AppIcon,
    Button,
    ButtonDirective,
    Card,
    FormsModule,
    InputText,
    RouterLink,
    Tag,
  ],
  templateUrl: './home-page.html',
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly behaviorApi = inject(BehaviorApiService);
  protected readonly authApi = inject(AuthApiService);
  protected readonly schoolContext = inject(SchoolContextService);

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly loadError = signal(false);
  protected excuseDrafts: Record<number, string> = {};

  /** Fixed order on purpose — every widget keeps its place, so the page can
   * be scanned by position. Only the empty ones move out of the grid (see
   * activeWidgets/emptyWidgets). */
  private readonly widgets = computed<Widget[]>(() => {
    const d = this.dashboard();
    if (!d) {
      return [];
    }
    const isAdmin = this.authApi.isAdmin();
    const isStaff = this.authApi.isTeacherOrAdmin();
    const widgets: Widget[] = [];
    if (isAdmin) {
      widgets.push({
        id: 'school',
        title: 'Přehled školy',
        icon: 'school',
        link: 'admin',
        linkLabel: 'Administrace',
        hasContent: d.schoolCounts !== null,
      });
    } else {
      widgets.push({
        id: 'timetable',
        title: 'Dnešní rozvrh',
        icon: 'timetable',
        link: 'timetable',
        linkLabel: 'Celý rozvrh',
        hasContent: d.todayLessons.length > 0,
      });
    }
    if (!isStaff) {
      widgets.push({
        id: 'grades',
        title: 'Poslední známky',
        icon: 'grades',
        link: 'grades',
        linkLabel: 'Všechny známky',
        hasContent: d.recentGrades.length > 0,
      });
    }
    widgets.push(
      {
        id: 'absences',
        title: isStaff ? 'Omluvenky ke schválení' : 'Nevyřízené absence',
        icon: 'absences',
        link: 'absences',
        linkLabel: 'Celá docházka',
        hasContent: d.pendingAbsences.length > 0,
      },
      {
        id: 'messages',
        title: 'Zprávy',
        icon: 'messages',
        link: 'messages',
        linkLabel: 'Všechny zprávy',
        hasContent: d.unreadMessageCount > 0,
      },
      {
        id: 'behavior',
        title: 'Chování',
        icon: 'behavior',
        link: 'behavior',
        linkLabel: 'Celé chování',
        hasContent: d.unreadBehaviorRecords.length > 0,
      },
      {
        id: 'events',
        title: 'Nejbližší akce',
        icon: 'events',
        link: 'events',
        linkLabel: 'Celý kalendář',
        hasContent: d.upcomingEvents.length > 0,
      },
    );
    return widgets;
  });

  protected readonly activeWidgets = computed(() =>
    this.widgets().filter((w) => w.hasContent),
  );
  protected readonly emptyWidgets = computed(() =>
    this.widgets().filter((w) => !w.hasContent),
  );

  constructor() {
    this.reload();
  }

  /** Unlike Parent, a Student may never submit their own excuse — the
   * backend rejects it too (AbsencesController.SubmitExcuse), this just
   * keeps the button from appearing in the first place. Same check as
   * absences-page.ts's canSubmitExcuse(). */
  protected canSubmitExcuse(): boolean {
    return !this.authApi.isStudent();
  }

  protected submitExcuse(absenceId: number): void {
    const reason = this.excuseDrafts[absenceId];
    if (!reason) {
      return;
    }
    this.absencesApi.submitExcuse(absenceId, reason).subscribe({
      next: () => {
        delete this.excuseDrafts[absenceId];
        this.reload();
      },
      error: (err) => console.error('submitExcuse failed', err),
    });
  }

  protected absenceStatusSeverity(
    status: AbsenceStatus,
  ): 'success' | 'warn' | 'danger' | 'secondary' {
    return ABSENCE_STATUS_SEVERITY[status];
  }

  protected absenceStatusLabel(status: AbsenceStatus): string {
    return ABSENCE_STATUS_LABEL[status];
  }

  /** Shows the end too (time always, date only when it differs from the
   * start date) — a single-day event previously only showed when it
   * started, hiding how long it actually runs. */
  protected formatEventRange(
    startAt: string,
    endAt: string,
    allDay: boolean,
  ): string {
    const start = new Date(startAt);
    const end = new Date(endAt);
    const sameDay = start.toDateString() === end.toDateString();
    const startDate = start.toLocaleDateString('cs-CZ');

    if (allDay) {
      return sameDay
        ? startDate
        : `${startDate}–${end.toLocaleDateString('cs-CZ')}`;
    }

    const timeOptions: Intl.DateTimeFormatOptions = {
      hour: '2-digit',
      minute: '2-digit',
    };
    const startTime = start.toLocaleTimeString('cs-CZ', timeOptions);
    const endTime = end.toLocaleTimeString('cs-CZ', timeOptions);
    if (sameDay) {
      return `${startDate} ${startTime}–${endTime}`;
    }
    return `${startDate} ${startTime} – ${end.toLocaleDateString('cs-CZ')} ${endTime}`;
  }

  protected recordTypeLabel(type: BehaviorRecordType): string {
    return RECORD_TYPE_LABELS[type];
  }

  protected recordTypeSeverity(
    type: BehaviorRecordType,
  ): 'success' | 'info' | 'warn' | 'danger' {
    return RECORD_TYPE_SEVERITY[type];
  }

  protected markBehaviorRecordRead(id: number): void {
    this.behaviorApi.markRecordRead(id).subscribe({
      next: () => this.reload(),
      error: (err) => console.error('markRecordRead failed', err),
    });
  }

  private reload(): void {
    this.dashboardApi.getDashboard().subscribe({
      next: (dashboard) => this.dashboard.set(dashboard),
      error: (err) => {
        console.error('getDashboard failed', err);
        this.loadError.set(true);
      },
    });
  }
}
