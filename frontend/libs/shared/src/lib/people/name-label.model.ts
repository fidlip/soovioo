/** How a pupil or parent appears to the caller when it isn't just their
 * full name — see backend `People/NameLabels.cs`. Absent = the full name. */
export interface NameLabel {
  /** Shown prominently: a nickname, or a parent's child's first name. */
  primary: string;
  /** The full name, shown subdued; null = `primary` alone. */
  secondary: string | null;
}

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
