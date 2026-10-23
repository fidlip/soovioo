/** Lower-cased, without diacritics — "Šalda" and "salda" match each other. */
export function foldForSearch(text: string): string {
  return text
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .toLowerCase();
}

/** Substring match on the e-mail, or on the phone with spaces and the Czech
 * +420 prefix ignored on both sides ("777 123" finds "+420777123456"). */
export function matchesContact(
  user: { email?: string | null; phone?: string | null },
  query: string,
): boolean {
  const phoneDigits = (text: string) =>
    text.replace(/[\s\-/]/g, '').replace(/^(\+|00)420/, '');
  const q = query.toLowerCase();
  if (user.email?.toLowerCase().includes(q)) {
    return true;
  }
  const qPhone = phoneDigits(q);
  return !!qPhone && !!user.phone && phoneDigits(user.phone).includes(qPhone);
}
