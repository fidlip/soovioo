import { VisibilityOption } from '@soovioo/shared';

export interface VisibilityOptionInfo {
  option: VisibilityOption;
  /** Administrace → Viditelnost údajů. */
  label: string;
  hint: string;
  /** Who may hide their own part, if the school allows it. */
  whoCanHide: string;
  /** Nastavení → Soukromí: the switch hiding the caller's own data. None
   * for the class list — that one is per pupil, not per account. */
  hideOwnLabel?: string;
}

/** The school's "Viditelnost údajů" options in display order. Photo options
 * exist on the backend already, but there are no photos yet, so they're
 * not offered. */
export const VISIBILITY_OPTIONS: VisibilityOptionInfo[] = [
  {
    option: 'TeacherContacts',
    label: 'Telefon a e-mail učitelů',
    hint: 'Rodiče uvidí na stránce Třída telefon a e-mail učitelů, kteří jejich dítě učí.',
    whoCanHide: 'učitelé',
    hideOwnLabel: 'Skrýt můj telefon a e-mail před rodiči',
  },
  {
    option: 'Classmates',
    label: 'Seznam spolužáků',
    hint: 'Žáci a rodiče uvidí na stránce Třída jména všech žáků ve třídě.',
    whoCanHide: 'žáci a rodiče (za své dítě)',
  },
  {
    option: 'ParentContacts',
    label: 'Ostatní rodiče ve třídě',
    hint: 'Rodiče uvidí na stránce Třída jména, telefony a e-maily ostatních rodičů ze třídy.',
    whoCanHide: 'rodiče',
    hideOwnLabel: 'Skrýt mé jméno a kontakt před ostatními rodiči ve třídě',
  },
];

export function visibilityOptionInfo(
  option: VisibilityOption,
): VisibilityOptionInfo | undefined {
  return VISIBILITY_OPTIONS.find((o) => o.option === option);
}
