import { Component, inject } from '@angular/core';
import { Popover } from '@openng/optimus-ui/popover';
import { DateFormatPipe } from '@soovioo/shared';
import { AppVersionService } from '../../app-version.service';

/** The deployed version at the bottom of the sidebar; a click shows the
 * pull requests it's made of, with links to GitHub. */
@Component({
  selector: 'app-deployed-version',
  imports: [DateFormatPipe, Popover],
  templateUrl: './deployed-version.html',
  styleUrl: './deployed-version.scss',
})
export class DeployedVersion {
  protected readonly info = inject(AppVersionService).info;
}
