import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { isTestInstance } from './app/main-domain';

// Tints the whole page green (styles.scss), before the app paints.
document.documentElement.classList.toggle('soovioo-test', isTestInstance());

bootstrapApplication(App, appConfig).catch((err) => console.error(err));
