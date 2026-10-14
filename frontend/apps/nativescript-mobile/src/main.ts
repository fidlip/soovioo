import {
  bootstrapApplication,
  provideNativeScriptHttpClient,
  provideNativeScriptRouter,
  runNativeScriptAngularApp,
} from '@nativescript/angular';
import { inject, provideAppInitializer, provideZonelessChangeDetection } from '@angular/core';
import { withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import { AuthApiService, API_BASE_URL, TokenStorage, authInterceptor } from '@sova/shared';
import { firstValueFrom } from 'rxjs';
import { routes } from './app.routes';
import { AppComponent } from './app.component';
import { NativeTokenStorage } from './native-token-storage';

runNativeScriptAngularApp({
  appModuleBootstrap: () => {
    return bootstrapApplication(AppComponent, {
      providers: [
        provideNativeScriptHttpClient(withInterceptorsFromDi(), withInterceptors([authInterceptor])),
        provideNativeScriptRouter(routes),
        provideZonelessChangeDetection(),
        // 10.0.2.2 is the Android emulator's alias for the host machine's localhost.
        { provide: API_BASE_URL, useValue: 'http://10.0.2.2:5080' },
        { provide: TokenStorage, useClass: NativeTokenStorage },
        // Stopgap until there's a real mobile login screen: auto-login as the
        // demo teacher account so the skeleton app can still reach the
        // now-[Authorize]-protected API.
        provideAppInitializer(() => {
          const authApi = inject(AuthApiService);
          if (authApi.isLoggedIn()) {
            return;
          }
          return firstValueFrom(
            authApi.login({ username: 'ucitel', password: 'ucitel123' }),
          ).catch((err) => console.error('auto-login failed', err));
        }),
      ],
    });
  },
});
