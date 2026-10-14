import {
  ApplicationConfig,
  provideBrowserGlobalErrorListeners,
} from '@angular/core';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter } from '@angular/router';
import { API_BASE_URL, TokenStorage, authInterceptor } from '@sova/shared';
import { appRoutes } from './app.routes';
import { LocalTokenStorage } from './local-token-storage';

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    provideRouter(appRoutes),
    provideHttpClient(withInterceptors([authInterceptor])),
    // Same host the page was loaded from, so this also works when a phone
    // on the LAN opens the dev server by IP instead of localhost.
    { provide: API_BASE_URL, useValue: `http://${window.location.hostname}:5080` },
    { provide: TokenStorage, useClass: LocalTokenStorage },
  ],
};
