import { Route } from '@angular/router';
import { AbsencesPage } from './absences-page/absences-page';
import { GradesPage } from './grades-page/grades-page';

export const appRoutes: Route[] = [
  { path: '', redirectTo: 'grades', pathMatch: 'full' },
  { path: 'grades', component: GradesPage },
  { path: 'absences', component: AbsencesPage },
];
