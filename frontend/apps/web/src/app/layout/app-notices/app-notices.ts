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
import {
  AuthApiService,
  SchoolYearsStore,
  SupportApiService,
} from '@soovioo/shared';
import { SchoolContextService } from '../../school-context';
import {
  SupportAsPick,
  SupportAsPicker,
} from '../../support-as/support-as-picker';
import { NotifyService } from '../../notify.service';
import { UnreadCountsService } from '../../unread-counts.service';
import { SupportPresenceBanners } from '../support-presence/support-presence';
import { SchoolYearWizard } from '../../school-year-wizard/school-year-wizard';
import { SessionSyncService } from '../../session-sync.service';
import { AppVersionService } from '../../app-version.service';
import { SupportTicketDialog } from '../../support-ticket/support-ticket-dialog';
import { SupportEndedDialog } from '../support-ended-dialog/support-ended-dialog';
import { SupportLeaveDialog } from '../support-leave-dialog/support-leave-dialog';
import { checkOnNavigation } from '../check-on-navigation';

/** Strips on top of every page: a newer version of the app was deployed,
 * this tab's session changed elsewhere, a
 * SuperAdmin is here in support mode — or in the user's account, or as
 * someone for the user's ticket ("Vstoupit jako…") — or the school has no
 * school year for today. */
@Component({
  selector: 'app-notices',
  templateUrl: './app-notices.html',
  styleUrl: './app-notices.scss',
  imports: [
    SchoolYearWizard,
    SupportAsPicker,
    SupportPresenceBanners,
    SupportEndedDialog,
    SupportLeaveDialog,
    SupportTicketDialog,
  ],
})
export class AppNotices {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionSync = inject(SessionSyncService);
  protected readonly outdated = inject(AppVersionService).outdated;

  private readonly schoolYears = inject(SchoolYearsStore);
  private readonly supportApi = inject(SupportApiService);
  private readonly notify = inject(NotifyService);
  private readonly schoolContext = inject(SchoolContextService);
  private readonly router = inject(Router);
  /** Refreshed with the badges (within a minute). */
  protected readonly supportPresence = inject(UnreadCountsService).support;

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
    this.router.events
      .pipe(
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

  protected switchAs(picker: SupportAsPicker): void {
    picker.open(this.supportApi.getSessionUsers(), {
      userId: this.authApi.getUserId(),
      roleId: this.authApi.getActiveRole()?.id ?? null,
    });
  }

  /** Every page decides what to show by who's in once — a full reload onto
   * Home, as when switching the role. */
  protected switchedAs(as: SupportAsPick): void {
    this.authApi.switchSupportAs(as.userId, as.roleId).subscribe({
      next: () =>
        window.location.assign(
          this.router.serializeUrl(
            this.router.createUrlTree(this.schoolContext.link('home')),
          ),
        ),
      error: (err) => this.notify.actionFailed('switchSupportAs failed', err),
    });
  }
}
