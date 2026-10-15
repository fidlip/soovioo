import { Route } from '@angular/router';
import { AbsencesPage } from './absences-page/absences-page';
import { AdminPage } from './admin-page/admin-page';
import { authGuard } from './auth.guard';
import { GradesPage } from './grades-page/grades-page';
import { LoginPage } from './login-page/login-page';
import { MessagesPage } from './messages-page/messages-page';
import { TimetablePage } from './timetable-page/timetable-page';

export const appRoutes: Route[] = [
  { path: '', redirectTo: 'grades', pathMatch: 'full' },
  { path: 'login', component: LoginPage },
  { path: 'grades', component: GradesPage, canActivate: [authGuard] },
  { path: 'absences', component: AbsencesPage, canActivate: [authGuard] },
  { path: 'timetable', component: TimetablePage, canActivate: [authGuard] },
  { path: 'messages', component: MessagesPage, canActivate: [authGuard] },
  { path: 'admin', component: AdminPage, canActivate: [authGuard] },
];
