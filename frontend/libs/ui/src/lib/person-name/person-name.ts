import { Component, input } from '@angular/core';

/** How a pupil or parent appears to the caller when it isn't just their
 * full name — see the app's backend `People/NameLabels.cs`. Absent = the
 * full name. */
export interface NameLabel {
  /** Shown prominently: a nickname, or a parent's child's first name. */
  primary: string;
  /** The full name, shown subdued; null = `primary` alone. */
  secondary: string | null;
}

/** A pupil's or parent's name as the caller sees it (`NameLabel` from the
 * API): a nickname prominent with the full name subdued — next to it or
 * under it — or just the full name. Takes the surrounding text's style;
 * only the full name next to a nickname is toned down. */
@Component({
  selector: 'app-person-name',
  templateUrl: './person-name.html',
  host: { class: 'min-w-0' },
})
export class PersonName {
  readonly name = input.required<string>();
  readonly label = input<NameLabel | null | undefined>(null);
  /** `stacked`: the full name on a line of its own (lists with room). */
  readonly layout = input<'inline' | 'stacked'>('inline');
}
