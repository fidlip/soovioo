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
    { provide: API_BASE_URL, useValue: 'http://localhost:5080' },
    { provide: TokenStorage, useClass: LocalTokenStorage },
  ],
};
