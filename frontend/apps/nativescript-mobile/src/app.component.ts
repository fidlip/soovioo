import { Component, NO_ERRORS_SCHEMA, inject } from '@angular/core';
import { PageRouterOutlet } from '@nativescript/angular';
import { PushService } from './push.service';

@Component({
  selector: 'ns-app',
  templateUrl: './app.component.html',
  imports: [PageRouterOutlet],
  schemas: [NO_ERRORS_SCHEMA],
})
export class AppComponent {
  constructor() {
    inject(PushService).start();
  }
}
