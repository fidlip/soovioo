/** Checks and normalizes e-mails and phone numbers people type in forms —
 * the same rules as the server (ContactFormats). An empty value is valid
 * and stands for none. */
export interface ContactParse {
  valid: boolean;
  /** Normalized; null when empty or invalid. */
  value: string | null;
}

export const INVALID_EMAIL = 'Neplatný e-mail.';
export const INVALID_PHONE =
  'Neplatný telefon — české číslo (777 123 456) nebo zahraniční s předvolbou (+421 …).';

const CZECH_PREFIX = '420';
const INVALID: ContactParse = { valid: false, value: null };
const EMPTY: ContactParse = { valid: true, value: null };

// No \p{…} regexes here: the mobile app's JS engine has no ICU.
const isLetter = (c: string) => c.toLowerCase() !== c.toUpperCase();
const isDigit = (c: string) => c >= '0' && c <= '9';

/** Trimmed, with the domain in lower case. */
export function parseEmail(input: string | null | undefined): ContactParse {
  const email = input?.trim();
  if (!email) {
    return EMPTY;
  }
  if (email.length > 254 || /[\s<>,;"()]/.test(email)) {
    return INVALID;
  }
  const at = email.indexOf('@');
  if (at < 0 || at !== email.lastIndexOf('@')) {
    return INVALID;
  }
  const local = email.slice(0, at);
  const domain = email.slice(at + 1);
  if (
    local.length === 0 ||
    local.length > 64 ||
    local.startsWith('.') ||
    local.endsWith('.') ||
    local.includes('..')
  ) {
    return INVALID;
  }
  const labels = domain.split('.');
  const tld = labels[labels.length - 1];
  if (
    labels.length < 2 ||
    labels.some(
      (l) =>
        l.length === 0 ||
        l.length > 63 ||
        l.startsWith('-') ||
        l.endsWith('-') ||
        [...l].some((c) => !isLetter(c) && !isDigit(c) && c !== '-'),
    ) ||
    tld.length < 2 ||
    ![...tld].every(isLetter)
  ) {
    return INVALID;
  }
  return { valid: true, value: `${local}@${domain.toLowerCase()}` };
}

/** Czech numbers as "+420 777 123 456" (with or without the prefix, also
 * "00420…"); foreign ones need "+" or "00" and keep their grouping,
 * separators turned into single spaces. */
export function parsePhone(input: string | null | undefined): ContactParse {
  const phone = input?.trim();
  if (!phone) {
    return EMPTY;
  }
  let plus = phone.startsWith('+');
  let groups = '';
  for (const c of plus ? phone.slice(1) : phone) {
    if (isDigit(c)) {
      groups += c;
    } else if (' -./()'.includes(c)) {
      if (groups.length > 0 && !groups.endsWith(' ')) {
        groups += ' ';
      }
    } else {
      return INVALID;
    }
  }
  groups = groups.trimEnd();
  if (!plus && groups.startsWith('00')) {
    plus = true;
    groups = groups.slice(2).trimStart();
  }
  const digits = groups.replace(/ /g, '');

  let national: string | null = null;
  if (!plus && digits.length === 9) {
    national = digits;
  } else if (
    digits.startsWith(CZECH_PREFIX) &&
    (plus || digits.length === 12)
  ) {
    national = digits.length === 12 ? digits.slice(3) : '';
  }
  if (national !== null) {
    return national.length === 9 && national[0] >= '2' && national[0] <= '9'
      ? {
          valid: true,
          value: `+${CZECH_PREFIX} ${national.slice(0, 3)} ${national.slice(3, 6)} ${national.slice(6)}`,
        }
      : INVALID;
  }
  if (!plus || digits.length < 8 || digits.length > 15 || digits[0] === '0') {
    return INVALID;
  }
  return { valid: true, value: `+${groups}` };
}
