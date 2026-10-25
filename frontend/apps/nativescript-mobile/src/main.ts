import {
  bootstrapApplication,
  provideNativeScriptHttpClient,
  provideNativeScriptRouter,
  runNativeScriptAngularApp,
} from '@nativescript/angular';
import { registerLocaleData } from '@angular/common';
import localeCs from '@angular/common/locales/cs';
import { LOCALE_ID, provideZonelessChangeDetection } from '@angular/core';
import { withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import {
  API_BASE_URL,
  APP_LOCALE,
  TokenStorage,
  authInterceptor,
} from '@soovioo/shared';
import { routes } from './app.routes';
import { AppComponent } from './app.component';
import { NativeTokenStorage } from './native-token-storage';

registerLocaleData(localeCs);

runNativeScriptAngularApp({
  appModuleBootstrap: () => {
    return bootstrapApplication(AppComponent, {
      providers: [
        provideNativeScriptHttpClient(
          withInterceptorsFromDi(),
          withInterceptors([authInterceptor]),
        ),
        provideNativeScriptRouter(routes),
        provideZonelessChangeDetection(),
        { provide: LOCALE_ID, useValue: APP_LOCALE },
        // 10.0.2.2 is the Android emulator's alias for the host machine's localhost.
        { provide: API_BASE_URL, useValue: 'http://10.0.2.2:5080' },
        { provide: TokenStorage, useClass: NativeTokenStorage },
      ],
    });
  },
});
