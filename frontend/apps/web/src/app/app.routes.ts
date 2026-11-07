import { Route } from '@angular/router';
import { authGuard } from './auth.guard';
import { LoginPage } from './login-page/login-page';
import { schoolExistsGuard } from './school-exists.guard';
import {
  DEFAULT_DEV_SLUG,
  DEMO_ENTRY_SLUG,
  isDemoEntryHost,
  isPathBasedTenant,
} from './school-context';

// Pages are lazy chunks (the login page aside — it's the usual first
// screen), so the first load doesn't download every agenda up front.
const schoolRoutes: Route[] = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'login', component: LoginPage, data: { hideHeader: true } },
  {
    path: 'accept-invite',
    loadComponent: () =>
      import('./accept-invite-page/accept-invite-page').then(
        (m) => m.AcceptInvitePage,
      ),
    data: { hideHeader: true },
  },
  {
    path: 'unsubscribe',
    loadComponent: () =>
      import('./unsubscribe-page/unsubscribe-page').then(
        (m) => m.UnsubscribePage,
      ),
    data: { hideHeader: true },
  },
  // Switching to a linked account — no guard: it's what logs in.
  {
    path: 'account/switch',
    loadComponent: () =>
      import('./account-switch-page/account-switch-page').then(
        (m) => m.AccountSwitchPage,
      ),
    data: { hideHeader: true },
  },
  // Linking accounts: at the other school, which asks to log in itself…
  {
    path: 'account/link',
    loadComponent: () =>
      import('./account-link-page/account-link-page').then(
        (m) => m.AccountLinkPage,
      ),
    data: { hideHeader: true },
  },
  // …and back where it started, finished by the account that asked.
  {
    path: 'account/link/done',
    loadComponent: () =>
      import('./account-link-page/account-link-done-page').then(
        (m) => m.AccountLinkDonePage,
      ),
    data: { hideHeader: true },
  },
  // SuperAdmin "Vstoupit do školy" — no guard: it's what logs in.
  {
    path: 'support/enter',
    loadComponent: () =>
      import('./support-enter-page/support-enter-page').then(
        (m) => m.SupportEnterPage,
      ),
    data: { hideHeader: true },
  },
  {
    path: 'home',
    loadComponent: () =>
      import('./home-page/home-page').then((m) => m.HomePage),
    canActivate: [authGuard],
  },
  {
    path: 'grades',
    loadComponent: () =>
      import('./grades-page/grades-page').then((m) => m.GradesPage),
    canActivate: [authGuard],
  },
  {
    path: 'absences',
    loadComponent: () =>
      import('./absences-page/absences-page').then((m) => m.AbsencesPage),
    canActivate: [authGuard],
  },
  {
    path: 'timetable',
    loadComponent: () =>
      import('./timetable-page/timetable-page').then((m) => m.TimetablePage),
    canActivate: [authGuard],
  },
  {
    path: 'events',
    loadComponent: () =>
      import('./events-page/events-page').then((m) => m.EventsPage),
    canActivate: [authGuard],
  },
  {
    path: 'messages',
    loadComponent: () =>
      import('./messages-page/messages-page').then((m) => m.MessagesPage),
    canActivate: [authGuard],
  },
  {
    path: 'homework',
    loadComponent: () =>
      import('./homework-page/homework-page').then((m) => m.HomeworkPage),
    canActivate: [authGuard],
  },
  {
    path: 'behavior',
    loadComponent: () =>
      import('./behavior-page/behavior-page').then((m) => m.BehaviorPage),
    canActivate: [authGuard],
  },
  {
    path: 'payments',
    loadComponent: () =>
      import('./payments-page/payments-page').then((m) => m.PaymentsPage),
    canActivate: [authGuard],
  },
  {
    path: 'people',
    loadComponent: () =>
      import('./class-page/people-page').then((m) => m.PeoplePage),
    canActivate: [authGuard],
  },
  // The page was called Třída before it got the whole school.
  { path: 'class', redirectTo: 'people' },
  {
    path: 'my-children',
    loadComponent: () =>
      import('./my-children-page/my-children-page').then(
        (m) => m.MyChildrenPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'admin',
    loadComponent: () =>
      import('./admin-page/admin-page').then((m) => m.AdminPage),
    canActivate: [authGuard],
  },
  {
    path: 'logs',
    loadComponent: () =>
      import('./logs-page/logs-page').then((m) => m.LogsPage),
    canActivate: [authGuard],
  },
  {
    path: 'settings',
    loadComponent: () =>
      import('./settings-page/settings-page').then((m) => m.SettingsPage),
    canActivate: [authGuard],
  },
];

// SuperAdmin isn't tied to any school, so it lives outside the `:schoolSlug`
// tree entirely: in prod that's the bare main domain (no subdomain), in dev
// there's no equivalent, so it gets its own reserved path. Must come before
// the `:schoolSlug` wildcard below — Angular matches routes in array order,
// and `:schoolSlug` would otherwise swallow "superadmin" as a literal slug.
const superAdminRoutes: Route[] = [
  // Not a SuperAdmin page, but outside the school tree for the same reason:
  // schoolExistsGuard shows it, and in dev `:schoolSlug` would take it.
  {
    path: 'school-not-found',
    loadComponent: () =>
      import('./school-not-found-page/school-not-found-page').then(
        (m) => m.SchoolNotFoundPage,
      ),
    data: { hideHeader: true },
  },
  {
    path: 'superadmin/login',
    component: LoginPage,
    data: { hideHeader: true },
  },
  {
    path: 'superadmin',
    loadComponent: () =>
      import('./super-admin-page/super-admin-page').then(
        (m) => m.SuperAdminPage,
      ),
    canActivate: [authGuard],
  },
  {
    path: 'superadmin/logs',
    loadComponent: () =>
      import('./logs-page/logs-page').then((m) => m.LogsPage),
    canActivate: [authGuard],
  },
  {
    path: 'superadmin/settings',
    loadComponent: () =>
      import('./settings-page/settings-page').then((m) => m.SettingsPage),
    canActivate: [authGuard],
  },
];

// "Vyzkoušet" — demo.<mainDomain> in production (the whole host is this
// one page), `/demo` in path mode, outside the school tree like the
// SuperAdmin area.
const demoEntryRoute: Route = {
  path: DEMO_ENTRY_SLUG,
  loadComponent: () =>
    import('./demo/demo-entry-page').then((m) => m.DemoEntryPage),
  data: { hideHeader: true },
};

// Dev/LAN: no real subdomain, so the school slug is the first path segment
// (`/skola/grades`). Real deployment: the slug comes from the subdomain
// (`skola.maindomain.cz/grades`) and these routes are mounted unprefixed.
export const appRoutes: Route[] = isDemoEntryHost()
  ? [
      { ...demoEntryRoute, path: '' },
      { path: '**', redirectTo: '' },
    ]
  : isPathBasedTenant()
    ? [
        ...superAdminRoutes,
        demoEntryRoute,
        { path: '', pathMatch: 'full', redirectTo: `/${DEFAULT_DEV_SLUG}` },
        {
          path: ':schoolSlug',
          canActivateChild: [schoolExistsGuard],
          children: schoolRoutes,
        },
      ]
    : [
        ...superAdminRoutes,
        {
          path: '',
          canActivateChild: [schoolExistsGuard],
          children: schoolRoutes,
        },
      ];
