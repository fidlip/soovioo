import type { NameLabel } from '@fidlip/soovioo-ui';

export type { NameLabel };

/** For plain text (select options, dialog titles): "Kuba (Jakub Novák)". */
export function nameText(
  name: string,
  label: NameLabel | null | undefined,
): string {
  if (!label) {
    return name;
  }
  return label.secondary
    ? `${label.primary} (${label.secondary})`
    : label.primary;
}
