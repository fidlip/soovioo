import { Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ButtonDirective } from '@openng/optimus-ui/button';
import { SchoolContextService } from '../../school-context';

/** Header for someone not logged in, on pages that aren't the auth pages. */
@Component({
  selector: 'app-public-header',
  imports: [ButtonDirective, RouterLink],
  templateUrl: './public-header.html',
  styleUrl: './public-header.scss',
})
export class PublicHeader {
  protected readonly schoolContext = inject(SchoolContextService);
}
