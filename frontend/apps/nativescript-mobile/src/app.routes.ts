import { Routes } from '@angular/router';
import { authGuard } from './auth.guard';
import { AbsencesComponent } from './features/absences/absences.component';
import { HomeComponent } from './features/home/home.component';
import { LoginComponent } from './features/login/login.component';
import { MessagesComponent } from './features/messages/messages.component';
import { SettingsComponent } from './features/settings/settings.component';
import { TimetableComponent } from './features/timetable/timetable.component';

export const routes: Routes = [
  { path: '', redirectTo: '/login', pathMatch: 'full' },
  { path: 'login', component: LoginComponent },
  { path: 'grades', component: HomeComponent, canActivate: [authGuard] },
  { path: 'absences', component: AbsencesComponent, canActivate: [authGuard] },
  { path: 'timetable', component: TimetableComponent, canActivate: [authGuard] },
  { path: 'messages', component: MessagesComponent, canActivate: [authGuard] },
  { path: 'settings', component: SettingsComponent, canActivate: [authGuard] },
];
