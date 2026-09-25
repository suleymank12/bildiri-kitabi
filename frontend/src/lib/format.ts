const locale = 'tr-TR';

const numberFormat = new Intl.NumberFormat(locale, { maximumFractionDigits: 1 });
const integerFormat = new Intl.NumberFormat(locale);
const dateFormat = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'long', year: 'numeric' });
const dateTimeFormat = new Intl.DateTimeFormat(locale, {
  day: 'numeric',
  month: 'long',
  year: 'numeric',
  hour: '2-digit',
  minute: '2-digit',
});

const KB = 1024;
const MB = KB * 1024;

/** "812 B", "48,3 KB", "9,8 MB" — Turkish decimal comma. */
export function formatBytes(bytes: number): string {
  if (bytes < KB) {
    return `${integerFormat.format(bytes)} B`;
  }

  if (bytes < MB) {
    return `${numberFormat.format(bytes / KB)} KB`;
  }

  return `${numberFormat.format(bytes / MB)} MB`;
}

export function formatInteger(value: number): string {
  return integerFormat.format(value);
}

/** "29 Eylül 2026" */
export function formatDate(value: string | Date): string {
  return dateFormat.format(typeof value === 'string' ? new Date(value) : value);
}

/** "29 Eylül 2026 14:05" */
export function formatDateTime(value: string | Date): string {
  return dateTimeFormat.format(typeof value === 'string' ? new Date(value) : value);
}

// Last-word vowel harmony of Turkish number names: 1 bir → 1'i, 3 üç → 3'ü, 6 altı → 6'sı, 10 on → 10'u, …
const unitSuffixes = ['ı', 'i', 'si', 'ü', 'ü', 'i', 'sı', 'si', 'i', 'u'];
const tensSuffixes = ['', 'u', 'si', 'u', 'ı', 'si', 'ı', 'i', 'i', 'ı'];

/** "7'si", "10'u", "3'ü" — the possessive of a number as in "10 dosyadan 7'si seçildi". */
export function withPossessive(value: number): string {
  return `${integerFormat.format(value)}${possessiveSuffix(value)}`;
}

/** Only the suffix: "'si" for 7, "'u" for 10. */
export function possessiveSuffix(value: number): string {
  const n = Math.abs(Math.trunc(value));
  let suffix: string;
  if (n === 0) {
    suffix = 'ı';
  } else if (n % 1000 === 0) {
    suffix = 'i';
  } else if (n % 100 === 0) {
    suffix = 'ü';
  } else if (n % 10 === 0) {
    suffix = tensSuffixes[(n / 10) % 10] ?? 'u';
  } else {
    suffix = unitSuffixes[n % 10] ?? 'i';
  }

  return `'${suffix}`;
}
