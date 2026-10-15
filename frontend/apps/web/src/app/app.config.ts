import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideAnimations } from '@angular/platform-browser/animations';
import { provideRouter, withRouterConfig } from '@angular/router';
import { provideOptimus } from '@openng/optimus-ui/config';
import { API_BASE_URL, TokenStorage, authInterceptor } from '@sova/shared';
import { appRoutes } from './app.routes';
import { LocalTokenStorage } from './local-token-storage';
import { sovaTheme } from './theme';

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
      theme: {
        preset: sovaTheme,
        options: {
          cssLayer: {
            name: 'optimus',
            order: 'tailwind-base, optimus, tailwind-utilities',
          },
          darkModeSelector: '.sova-dark',
        },
      },
    }),
    // Same host the page was loaded from, so this also works when a phone
    // on the LAN opens the dev server by IP instead of localhost.
    { provide: API_BASE_URL, useValue: `http://${window.location.hostname}:5080` },
    { provide: TokenStorage, useClass: LocalTokenStorage },
  ],
};
