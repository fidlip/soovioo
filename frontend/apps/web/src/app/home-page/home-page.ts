import { Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { InputText } from '@openng/optimus-ui/inputtext';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AbsenceStatus,
  AbsencesApiService,
  AuthApiService,
  Dashboard,
  DashboardApiService,
} from '@sova/shared';
import { SchoolContextService } from '../school-context';

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

/** Role-specific widget set, per the roadmap's "Home page (dashboard)" spec:
 * Parent/Student get their own schedule/grades/absences; Teacher/Admin get
 * the operational view (today's lessons taught, absences awaiting review);
 * Admin additionally gets school-wide counts. Every widget here mirrors
 * what the equivalent full agenda page would show — see DashboardController
 * for the shared access-control reasoning. */
@Component({
  selector: 'app-home-page',
  imports: [Button, Card, FormsModule, InputText, Tag],
  templateUrl: './home-page.html',
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly absencesApi = inject(AbsencesApiService);
  private readonly router = inject(Router);
  protected readonly authApi = inject(AuthApiService);
  protected readonly schoolContext = inject(SchoolContextService);

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly loadError = signal(false);
  protected excuseDrafts: Record<number, string> = {};

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

  protected goTo(path: string): void {
    this.router.navigate(this.schoolContext.link(path));
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
