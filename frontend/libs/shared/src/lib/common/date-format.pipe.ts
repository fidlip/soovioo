import { LOCALE_ID, Pipe, PipeTransform, inject } from '@angular/core';
import { DateFormat, formatDate } from './date-format';

/** `{{ at | dateFormat: 'dateTime' }}` — see {@link formatDate}. Empty for
 * a missing value. */
@Pipe({ name: 'dateFormat' })
export class DateFormatPipe implements PipeTransform {
  private readonly locale = inject(LOCALE_ID);

  transform(
    value: string | Date | null | undefined,
    format: DateFormat,
  ): string {
    return value == null ? '' : formatDate(value, format, this.locale);
  }
}
