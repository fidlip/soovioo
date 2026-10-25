import { registerLocaleData } from '@angular/common';
import localeCs from '@angular/common/locales/cs';
import {
  ApplicationConfig,
  LOCALE_ID,
  inject,
  provideAppInitializer,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import {
  PreloadAllModules,
  provideRouter,
  withPreloading,
  withRouterConfig,
} from '@angular/router';
import {
  ConfirmationService,
  MessageService,
  Translation,
} from '@openng/optimus-ui/api';
import { provideOptimus } from '@openng/optimus-ui/config';
import {
  API_BASE_URL,
  APP_LOCALE,
  AUTH_REFRESH_VIA_COOKIE,
  AuthApiService,
  CURRENT_SCHOOL_SLUG,
  TokenStorage,
  authInterceptor,
} from '@soovioo/shared';
import { environment } from '../environments/environment';
import { appRoutes } from './app.routes';
import { MemoryTokenStorage } from './memory-token-storage';
import { currentSchoolSlugFromUrl } from './school-context';
import { serverErrorInterceptor } from './server-error.interceptor';
import { sooviooTheme } from './theme';

// optimus-ui (PrimeNG) components with their own locale-dependent text —
// the DatePicker's month/day names and "today" button, for now — don't
// follow Angular's LOCALE_ID or a page's `lang` attribute; they need this
// explicit translation object, same as FullCalendar needs its own
// `@fullcalendar/core/locales/cs` import (see events-page.ts).
registerLocaleData(localeCs);

const csTranslation: Translation = {
  dayNames: [
    'neděle',
    'pondělí',
    'úterý',
    'středa',
    'čtvrtek',
    'pátek',
    'sobota',
  ],
  dayNamesShort: ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'],
  dayNamesMin: ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'],
  monthNames: [
    'leden',
    'únor',
    'březen',
    'duben',
    'květen',
    'červen',
    'červenec',
    'srpen',
    'září',
    'říjen',
    'listopad',
    'prosinec',
  ],
  monthNamesShort: [
    'led',
    'úno',
    'bře',
    'dub',
    'kvě',
    'čvn',
    'čvc',
    'srp',
    'zář',
    'říj',
    'lis',
    'pro',
  ],
  today: 'Dnes',
  clear: 'Vymazat',
  firstDayOfWeek: 1,
  dateFormat: 'dd.mm.yy',
  weekHeader: 'Týd',
};

const devApiPort = () => 5080 + (Number(window.location.port || 4200) - 4200);

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: APP_LOCALE },
    // 'always': a route nested under `:schoolSlug` sees that param on its
    // own paramMap too, so components/guards don't need to walk up the
    // route tree to find it.
    provideRouter(
      appRoutes,
      withRouterConfig({ paramsInheritanceStrategy: 'always' }),
      // Pages are lazy (app.routes.ts) to keep the first load small; once
      // it's done the rest download in the background, so moving between
      // agendas doesn't wait on the network.
      withPreloading(PreloadAllModules),
    ),
    provideHttpClient(
      withInterceptors([authInterceptor, serverErrorInterceptor]),
    ),
    MessageService,
    ConfirmationService,
    provideOptimus({
      translation: csTranslation,
      theme: {
        preset: sooviooTheme,
        options: {
          cssLayer: {
            name: 'optimus',
            order: 'tailwind-base, optimus, tailwind-utilities',
          },
          darkModeSelector: '.soovioo-dark',
        },
      },
    }),
    // Production: same origin — the reverse proxy (deploy/Caddyfile) sends
    // /api/* to the backend on every school subdomain, so no CORS. Dev: the
    // backend's own port on the host the page was loaded from, so this also
    // works when a phone on the LAN opens the dev server by IP.
    {
      provide: API_BASE_URL,
      useValue: environment.production
        ? window.location.origin
        : `http://${window.location.hostname}:${devApiPort()}`,
    },
    { provide: TokenStorage, useClass: MemoryTokenStorage },
    // Refresh token as an httpOnly cookie; the access token stays in
    // memory, so every page load first restores it from that cookie —
    // before the router's auth guard looks at isLoggedIn().
    { provide: AUTH_REFRESH_VIA_COOKIE, useValue: true },
    // Lets a SuperAdmin's support-mode session survive reloads of the
    // school's page (see SupportSessionService on the backend).
    {
      provide: CURRENT_SCHOOL_SLUG,
      useValue: () => currentSchoolSlugFromUrl(),
    },
    provideAppInitializer(() => inject(AuthApiService).restoreSession()),
  ],
};
