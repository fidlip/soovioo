import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter, withRouterConfig } from '@angular/router';
import { Translation } from '@openng/optimus-ui/api';
import { provideOptimus } from '@openng/optimus-ui/config';
import { API_BASE_URL, TokenStorage, authInterceptor } from '@soovioo/shared';
import { appRoutes } from './app.routes';
import { LocalTokenStorage } from './local-token-storage';
import { sooviooTheme } from './theme';

// optimus-ui (PrimeNG) components with their own locale-dependent text —
// the DatePicker's month/day names and "today" button, for now — don't
// follow Angular's LOCALE_ID or a page's `lang` attribute; they need this
// explicit translation object, same as FullCalendar needs its own
// `@fullcalendar/core/locales/cs` import (see events-page.ts).
const csTranslation: Translation = {
  dayNames: ['neděle', 'pondělí', 'úterý', 'středa', 'čtvrtek', 'pátek', 'sobota'],
  dayNamesShort: ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'],
  dayNamesMin: ['ne', 'po', 'út', 'st', 'čt', 'pá', 'so'],
  monthNames: [
    'leden', 'únor', 'březen', 'duben', 'květen', 'červen',
    'červenec', 'srpen', 'září', 'říjen', 'listopad', 'prosinec',
  ],
  monthNamesShort: ['led', 'úno', 'bře', 'dub', 'kvě', 'čvn', 'čvc', 'srp', 'zář', 'říj', 'lis', 'pro'],
  today: 'Dnes',
  clear: 'Vymazat',
  firstDayOfWeek: 1,
  dateFormat: 'dd.mm.yy',
  weekHeader: 'Týd',
};

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // 'always': a route nested under `:schoolSlug` sees that param on its
    // own paramMap too, so components/guards don't need to walk up the
    // route tree to find it.
    provideRouter(appRoutes, withRouterConfig({ paramsInheritanceStrategy: 'always' })),
    provideHttpClient(withInterceptors([authInterceptor])),
    provideAnimations(),
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
    // Same host the page was loaded from, so this also works when a phone
    // on the LAN opens the dev server by IP instead of localhost.
    { provide: API_BASE_URL, useValue: `http://${window.location.hostname}:5080` },
    { provide: TokenStorage, useClass: LocalTokenStorage },
  ],
};
