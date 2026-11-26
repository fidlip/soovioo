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
  MySchoolApiService,
  School,
  SchoolYearsStore,
  SupportApiService,
} from '@soovioo/shared';
import { SchoolContextService } from '../../school-context';
import {
  SupportAsPick,
  SupportAsPicker,
} from '../../support-as/support-as-picker';
import { NotifyService } from '@fidlip/soovioo-ui';
import { UnreadCountsService } from '../../unread-counts.service';
import { SupportPresenceBanners } from '../support-presence/support-presence';
import { SchoolYearWizard } from '../../school-year-wizard/school-year-wizard';
import { SchoolSetupWizard } from '../../school-setup-wizard/school-setup-wizard';
import { SessionSyncService } from '../../session-sync.service';
import { AppVersionService } from '../../app-version.service';
import { SupportEndedDialog } from '../support-ended-dialog/support-ended-dialog';
import { SupportLeaveDialog } from '../support-leave-dialog/support-leave-dialog';
import { checkOnNavigation } from '../check-on-navigation';

/** Strips on top of every page: a newer version of the app was deployed,
 * this tab's session changed elsewhere, a
 * SuperAdmin is here in support mode — or in the user's account, or as
 * someone for the user's ticket ("Vstoupit jako…") — or the school has no
 * school year for today. A new school's admin first goes through
 * "Nastavení školy", the year's wizard comes after it. */
@Component({
  selector: 'app-notices',
  templateUrl: './app-notices.html',
  styleUrl: './app-notices.scss',
  imports: [
    SchoolSetupWizard,
    SchoolYearWizard,
    SupportAsPicker,
    SupportPresenceBanners,
    SupportEndedDialog,
    SupportLeaveDialog,
  ],
})
export class AppNotices {
  protected readonly authApi = inject(AuthApiService);
  protected readonly sessionSync = inject(SessionSyncService);
  protected readonly outdated = inject(AppVersionService).outdated;

  private readonly schoolYears = inject(SchoolYearsStore);
  private readonly supportApi = inject(SupportApiService);
  private readonly mySchoolApi = inject(MySchoolApiService);
  private readonly notify = inject(NotifyService);
  private readonly schoolContext = inject(SchoolContextService);
  private readonly router = inject(Router);
  /** Refreshed with the badges (within a minute). */
  protected readonly supportPresence = inject(UnreadCountsService).support;

  private readonly wizard = viewChild(SchoolYearWizard);
  private readonly setupWizard = viewChild(SchoolSetupWizard);
  /** The school whose admin hasn't gone through "Nastavení školy" yet;
   * undefined until known (or for whoever doesn't administer it). */
  protected readonly setupPending = signal<School | null | undefined>(
    undefined,
  );
  /** Who is signed in, as of the last navigation: a school's account (not
   * a SuperAdmin outside support mode) and whether it may found a year. */
  private readonly inSchool = signal(false);
  protected readonly canFound = signal(false);
  private session = '';
  private offered = false;
  private setupOffered = false;
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
    effect(() => {
      const wizard = this.setupWizard();
      const school = this.setupPending();
      if (wizard && school && !this.setupOffered) {
        this.setupOffered = true;
        wizard.open(school);
      }
    });
    // Whoever may found the year gets the wizard once, on their own — once
    // the school is set up.
    effect(() => {
      const wizard = this.wizard();
      if (
        wizard &&
        this.noSchoolYear() &&
        this.canFound() &&
        this.setupPending() === null &&
        !this.offered
      ) {
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
      this.setupOffered = false;
      this.schoolYears.clear();
      this.checkSetup();
    }
    this.inSchool.set(role !== null);
    this.canFound.set(role !== null && this.authApi.can('SchoolYear'));
    if (role) {
      this.schoolYears.ensure();
    }
  }

  /** Support mode doesn't get the wizard — it's the school's own step. */
  private checkSetup(): void {
    this.setupPending.set(undefined);
    if (
      !this.session ||
      this.authApi.getSupportInfo() ||
      !this.authApi.can('Administration')
    ) {
      this.setupPending.set(null);
      return;
    }
    const session = this.session;
    this.mySchoolApi.getMySchool().subscribe({
      next: (school) => {
        if (session === this.session) {
          this.setupPending.set(school.setupCompletedAt ? null : school);
        }
      },
      error: (err) => console.error('getMySchool failed', err),
    });
  }

  /** The new name and type come with a fresh token, and every page reads
   * them once — a full reload, as when switching the role. The year's
   * wizard opens after it on its own. */
  protected setupCompleted(): void {
    window.location.reload();
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
