import { Component, input } from '@angular/core';
import { DateFormatPipe, Person } from '@soovioo/shared';
import { TermPipe } from '../term.pipe';

/** A person and their profiles, read only, in a frame — actions (edit,
 * remove) go into the header via content projection. */
@Component({
  selector: 'app-person-card',
  imports: [DateFormatPipe, TermPipe],
  template: `
    @let p = person();
    <div
      class="flex flex-col gap-2 rounded-md border border-surface-200 p-3 text-sm dark:border-surface-700"
      role="group"
      [attr.aria-label]="'Osoba ' + p.name"
    >
      <div class="flex items-start justify-between gap-2">
        <div>
          <div class="font-medium">{{ p.name }}</div>
          <div class="text-surface-500">
            {{ [p.phone, p.email].filter(isSet).join(' · ') || 'Bez kontaktu' }}
          </div>
        </div>
        <div class="flex gap-1"><ng-content /></div>
      </div>
      @if (p.teacher; as teacher) {
        <div>
          <span class="font-medium">Učitel:</span>
          aprobace {{ teacher.qualification || '—' }}
          @if (teacher.classTeacherOf.length > 0) {
            · třídní {{ teacher.classTeacherOf.join(', ') }}
          }
        </div>
      }
      @if (p.parent; as parent) {
        <div>
          <span class="font-medium">Rodič:</span>
          {{
            parent.children.length > 0
              ? parent.children.map(childName).join(', ')
              : 'bez přiřazených dětí'
          }}
        </div>
      }
      @if (p.student; as student) {
        <div>
          <span class="font-medium">{{ 'Žák' | term }}:</span>
          {{ student.schoolClassName }}
          @if (student.dateOfBirth) {
            · nar. {{ student.dateOfBirth | dateFormat: 'date' }}
          }
          @if (student.leftAt) {
            · končí k {{ student.leftAt | dateFormat: 'date' }}
          }
        </div>
      }
      @if (!p.teacher && !p.parent && !p.student) {
        <div class="text-surface-500">Bez profilu učitele, rodiče či žáka.</div>
      }
    </div>
  `,
})
export class PersonCard {
  readonly person = input.required<Person>();

  protected readonly isSet = (value: string | null) => !!value;
  protected readonly childName = (child: { name: string }) => child.name;
}
