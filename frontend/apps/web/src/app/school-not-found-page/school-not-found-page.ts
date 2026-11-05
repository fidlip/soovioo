import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { Card } from '@openng/optimus-ui/card';

/** What schoolExistsGuard shows for an unknown school slug. */
@Component({
  selector: 'app-school-not-found-page',
  imports: [Card],
  templateUrl: './school-not-found-page.html',
})
export class SchoolNotFoundPage {
  protected readonly slug =
    inject(ActivatedRoute).snapshot.queryParamMap.get('slug');
}
