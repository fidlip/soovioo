import { Pipe, PipeTransform } from '@angular/core';
import { NameLabel, nameText } from './name-label.model';

/** `{{ item.studentName | nameText: item.studentLabel }}` — see {@link nameText}. */
@Pipe({ name: 'nameText' })
export class NameTextPipe implements PipeTransform {
  transform(
    name: string | null | undefined,
    label: NameLabel | null | undefined,
  ): string {
    return name == null ? '' : nameText(name, label);
  }
}
