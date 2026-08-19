/**
 * Central locale registry using canonical BCP 47 identifiers.
 * The dashboard ships an English baseline; additional locales can be added
 * to resources.js and listed here without touching the rest of the app.
 */

export const LOCALE_STORAGE_KEY = 'pneuma.locale';
export const DEFAULT_LOCALE = 'en';

export const SUPPORTED_LOCALES = [
  { code: 'en', label: 'English', nativeLabel: 'English', dir: 'ltr' }
];

/** Alias normalization rules (regional variants collapse to a base locale). */
const ALIASES = {
  'en-us': 'en',
  'en-gb': 'en',
  'en-ca': 'en',
  'en-au': 'en'
};

export function normalizeLocale(input) {
  const value = String(input || '').trim().toLowerCase();
  if (!value) return DEFAULT_LOCALE;
  if (ALIASES[value]) return ALIASES[value];
  const exact = SUPPORTED_LOCALES.find((l) => l.code.toLowerCase() === value);
  if (exact) return exact.code;
  const base = value.split('-')[0];
  const baseMatch = SUPPORTED_LOCALES.find((l) => l.code.toLowerCase() === base);
  return baseMatch ? baseMatch.code : DEFAULT_LOCALE;
}

export function localeMeta(code) {
  return SUPPORTED_LOCALES.find((l) => l.code === normalizeLocale(code)) || SUPPORTED_LOCALES[0];
}
