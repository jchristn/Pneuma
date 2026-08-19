// Central locale registry using canonical BCP 47 identifiers.
export const STORAGE_KEY = 'pneuma.locale';

export const DEFAULT_LOCALE = 'en';

export const SUPPORTED_LOCALES = [
  { id: 'en', label: 'English', dir: 'ltr' }
];

// Alias normalization rules (e.g. en-US -> en).
const ALIASES = {
  'en-us': 'en',
  'en-gb': 'en'
};

export function normalizeLocale(locale) {
  if (!locale) return DEFAULT_LOCALE;
  const lower = String(locale).toLowerCase();
  if (ALIASES[lower]) return ALIASES[lower];
  const base = lower.split('-')[0];
  const match = SUPPORTED_LOCALES.find((l) => l.id === lower || l.id === base);
  return match ? match.id : DEFAULT_LOCALE;
}

export function getDirection(locale) {
  const normalized = normalizeLocale(locale);
  const entry = SUPPORTED_LOCALES.find((l) => l.id === normalized);
  return entry ? entry.dir : 'ltr';
}
