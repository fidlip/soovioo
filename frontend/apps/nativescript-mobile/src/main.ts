import {
  bootstrapApplication,
  provideNativeScriptHttpClient,
  provideNativeScriptRouter,
  runNativeScriptAngularApp,
} from '@nativescript/angular';
import { provideZonelessChangeDetection } from '@angular/core';
import { withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import { API_BASE_URL, TokenStorage, authInterceptor } from '@sova/shared';
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
      ],
    });
  },
});
