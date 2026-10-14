import {
  bootstrapApplication,
  provideNativeScriptHttpClient,
  provideNativeScriptRouter,
  runNativeScriptAngularApp,
} from '@nativescript/angular';
import { provideZonelessChangeDetection } from '@angular/core';
import { withInterceptorsFromDi } from '@angular/common/http';
import { API_BASE_URL } from '@sova/shared';
import { routes } from './app.routes';
import { AppComponent } from './app.component';

runNativeScriptAngularApp({
  appModuleBootstrap: () => {
    return bootstrapApplication(AppComponent, {
      providers: [
        provideNativeScriptHttpClient(withInterceptorsFromDi()),
        provideNativeScriptRouter(routes),
        provideZonelessChangeDetection(),
        // 10.0.2.2 is the Android emulator's alias for the host machine's localhost.
        { provide: API_BASE_URL, useValue: 'http://10.0.2.2:5080' },
      ],
    });
  },
});
