import { Route } from '@angular/router';
import { AbsencesPage } from './absences-page/absences-page';
import { AdminPage } from './admin-page/admin-page';
import { authGuard } from './auth.guard';
import { BehaviorPage } from './behavior-page/behavior-page';
import { EventsPage } from './events-page/events-page';
import { GradesPage } from './grades-page/grades-page';
import { HomePage } from './home-page/home-page';
import { HomeworkPage } from './homework-page/homework-page';
import { LoginPage } from './login-page/login-page';
import { MessagesPage } from './messages-page/messages-page';
import { PaymentsPage } from './payments-page/payments-page';
import { DEFAULT_DEV_SLUG, isPathBasedTenant } from './school-context';
import { SettingsPage } from './settings-page/settings-page';
import { SuperAdminPage } from './super-admin-page/super-admin-page';
import { TimetablePage } from './timetable-page/timetable-page';

const schoolRoutes: Route[] = [
  { path: '', redirectTo: 'home', pathMatch: 'full' },
  { path: 'login', component: LoginPage },
  { path: 'home', component: HomePage, canActivate: [authGuard] },
  { path: 'grades', component: GradesPage, canActivate: [authGuard] },
  { path: 'absences', component: AbsencesPage, canActivate: [authGuard] },
  { path: 'timetable', component: TimetablePage, canActivate: [authGuard] },
  { path: 'events', component: EventsPage, canActivate: [authGuard] },
  { path: 'messages', component: MessagesPage, canActivate: [authGuard] },
  { path: 'homework', component: HomeworkPage, canActivate: [authGuard] },
  { path: 'behavior', component: BehaviorPage, canActivate: [authGuard] },
  { path: 'payments', component: PaymentsPage, canActivate: [authGuard] },
  { path: 'admin', component: AdminPage, canActivate: [authGuard] },
  { path: 'settings', component: SettingsPage, canActivate: [authGuard] },
];

// SuperAdmin isn't tied to any school, so it lives outside the `:schoolSlug`
// tree entirely: in prod that's the bare main domain (no subdomain), in dev
// there's no equivalent, so it gets its own reserved path. Must come before
// the `:schoolSlug` wildcard below — Angular matches routes in array order,
// and `:schoolSlug` would otherwise swallow "superadmin" as a literal slug.
const superAdminRoutes: Route[] = [
  { path: 'superadmin/login', component: LoginPage },
  { path: 'superadmin', component: SuperAdminPage, canActivate: [authGuard] },
  {
    path: 'superadmin/settings',
    component: SettingsPage,
    canActivate: [authGuard],
  },
];

// Dev/LAN: no real subdomain, so the school slug is the first path segment
// (`/demo/grades`). Real deployment: the slug comes from the subdomain
// (`demo.maindomain.cz/grades`) and these routes are mounted unprefixed.
export const appRoutes: Route[] = isPathBasedTenant()
  ? [
      ...superAdminRoutes,
      { path: '', pathMatch: 'full', redirectTo: `/${DEFAULT_DEV_SLUG}` },
      { path: ':schoolSlug', children: schoolRoutes },
    ]
  : [...superAdminRoutes, ...schoolRoutes];
