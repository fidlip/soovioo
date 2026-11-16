import {
  Component,
  computed,
  effect,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';
import { AuthApiService, SchoolYearsStore } from '@soovioo/shared';
import { SchoolYearWizard } from '../../school-year-wizard/school-year-wizard';
import { SessionSyncService } from '../../session-sync.service';
import { SupportTicketDialog } from '../../support-ticket/support-ticket-dialog';
import { SupportEndedDialog } from '../support-ended-dialog/support-ended-dialog';
import { SupportLeaveDialog } from '../support-leave-dialog/support-leave-dialog';
import { checkOnNavigation } from '../check-on-navigation';

/** Strips on top of every page: this tab's session changed elsewhere, a
 * SuperAdmin is here in support mode, or the school has no school year for
 * today. */
@Component({
  selector: 'app-notices',
  templateUrl: './app-notices.html',
  styleUrl: './app-notices.scss',
  imports: [
    SchoolYearWizard,
    SupportEndedDialog,
    SupportLeaveDialog,
    SupportTicketDialog,
  ],
})
export class AppNotices {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionSync = inject(SessionSyncService);

  private readonly schoolYears = inject(SchoolYearsStore);
  private readonly wizard = viewChild(SchoolYearWizard);
  /** Who is signed in, as of the last navigation: a school's account (not
   * a SuperAdmin outside support mode) and whether it may found a year. */
  private readonly inSchool = signal(false);
  protected readonly canFound = signal(false);
  private session = '';
  private offered = false;
  /** Signed in at a school that has no school year for today. */
  protected readonly noSchoolYear = computed(
    () => this.inSchool() && this.schoolYears.missing(),
  );

  constructor() {
    checkOnNavigation();
    inject(Router)
      .events.pipe(
        filter((e) => e instanceof NavigationEnd),
        takeUntilDestroyed(),
      )
      .subscribe(() => this.syncSession());
    // Whoever may found the year gets the wizard once, on their own.
    effect(() => {
      const wizard = this.wizard();
      if (wizard && this.noSchoolYear() && this.canFound() && !this.offered) {
        this.offered = true;
        wizard.open();
      }
    });
  }

  private syncSession(): void {
    const role = this.authApi.isLoggedIn()
      ? this.authApi.getActiveRole()
      : null;
    const session = role
      ? `${this.authApi.getUserId()}/${role.id}/${this.authApi.getSchoolName()}`
      : '';
    if (session !== this.session) {
      this.session = session;
      this.offered = false;
      this.schoolYears.clear();
    }
    this.inSchool.set(role !== null);
    this.canFound.set(role !== null && this.authApi.can('SchoolYear'));
    if (role) {
      this.schoolYears.ensure();
    }
  }

  protected foundSchoolYear(): void {
    this.wizard()?.open();
  }

  protected reload(): void {
    window.location.reload();
  }
}
