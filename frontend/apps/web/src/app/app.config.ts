import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, withRouterConfig } from '@angular/router';
import { API_BASE_URL, TokenStorage, authInterceptor } from '@sova/shared';
import { appRoutes } from './app.routes';
import { LocalTokenStorage } from './local-token-storage';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    // 'always': a route nested under `:schoolSlug` sees that param on its
    // own paramMap too, so components/guards don't need to walk up the
    // route tree to find it.
    provideRouter(appRoutes, withRouterConfig({ paramsInheritanceStrategy: 'always' })),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Same host the page was loaded from, so this also works when a phone
    // on the LAN opens the dev server by IP instead of localhost.
    { provide: API_BASE_URL, useValue: `http://${window.location.hostname}:5080` },
    { provide: TokenStorage, useClass: LocalTokenStorage },
  ],
};
