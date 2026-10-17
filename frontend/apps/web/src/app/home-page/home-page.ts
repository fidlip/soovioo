import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Button } from '@openng/optimus-ui/button';
import { Card } from '@openng/optimus-ui/card';
import { Tag } from '@openng/optimus-ui/tag';
import {
  AbsenceStatus,
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
  imports: [Button, Card, Tag],
  templateUrl: './home-page.html',
})
export class HomePage {
  private readonly dashboardApi = inject(DashboardApiService);
  private readonly router = inject(Router);
  protected readonly authApi = inject(AuthApiService);
  protected readonly schoolContext = inject(SchoolContextService);

  protected readonly dashboard = signal<Dashboard | null>(null);
  protected readonly loadError = signal(false);

  constructor() {
    this.dashboardApi.getDashboard().subscribe({
      next: (dashboard) => this.dashboard.set(dashboard),
      error: (err) => {
        console.error('getDashboard failed', err);
        this.loadError.set(true);
      },
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

  protected formatDateTime(value: string, allDay: boolean): string {
    const date = new Date(value);
    const datePart = date.toLocaleDateString('cs-CZ');
    if (allDay) {
      return datePart;
    }
    return `${datePart} ${date.toLocaleTimeString('cs-CZ', { hour: '2-digit', minute: '2-digit' })}`;
  }

  protected goTo(path: string): void {
    this.router.navigate(this.schoolContext.link(path));
  }
}
