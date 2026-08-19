/**
 * Central locale registry using canonical BCP 47 identifiers.
 */
export const STORAGE_KEY = 'pneuma.subject.locale';
export const DEFAULT_LOCALE = 'en';

export const SUPPORTED_LOCALES = [
  { id: 'en', label: 'English', dir: 'ltr' },
  { id: 'es', label: 'Espanol', dir: 'ltr' }
];

// Normalize aliases (e.g. en-US -> en) to a supported locale id.
const ALIASES = {
  'en-us': 'en',
  'en-gb': 'en',
  'es-es': 'es',
  'es-mx': 'es'
};

export function normalizeLocale(locale) {
  if (!locale) return DEFAULT_LOCALE;
  const lower = String(locale).toLowerCase();
  if (ALIASES[lower]) return ALIASES[lower];
  const base = lower.split('-')[0];
  const match = SUPPORTED_LOCALES.find((l) => l.id === base);
  return match ? match.id : DEFAULT_LOCALE;
}

export function getDirection(locale) {
  const match = SUPPORTED_LOCALES.find((l) => l.id === normalizeLocale(locale));
  return match ? match.dir : 'ltr';
}
