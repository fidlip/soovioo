import { Component } from '@angular/core';
import { isTestInstance } from '../main-domain';

/** Strip on top of every page of the test instance, so it isn't mistaken
 * for the real one. */
@Component({
  selector: 'app-test-banner',
  templateUrl: './test-banner.html',
})
export class TestBanner {
  protected readonly shown = isTestInstance();
}
