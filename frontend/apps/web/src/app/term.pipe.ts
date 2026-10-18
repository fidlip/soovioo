import { Pipe, PipeTransform, inject } from '@angular/core';
import { AuthApiService, localizeStudentTerm } from '@soovioo/shared';

/** `{{ 'Všichni žáci' | term }}` — "žák"/"student" per the school's type
 * (ZŠ/SŠ), see `localizeStudentTerm`. Pure: the school type only changes
 * with a new token, and pages re-render on navigation anyway. */
@Pipe({ name: 'term' })
export class TermPipe implements PipeTransform {
  private readonly authApi = inject(AuthApiService);

  transform(text: string): string {
    return localizeStudentTerm(text, this.authApi.getSchoolType());
  }
}
