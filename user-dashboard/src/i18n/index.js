import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import resources from './resources.js';
import {
  DEFAULT_LOCALE,
  LOCALE_STORAGE_KEY,
  SUPPORTED_LOCALES,
  localeMeta,
  normalizeLocale
} from './localeRegistry.js';

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: DEFAULT_LOCALE,
    supportedLngs: SUPPORTED_LOCALES.map((l) => l.code),
    nonExplicitSupportedLngs: true,
    load: 'languageOnly',
    interpolation: { escapeValue: false },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: LOCALE_STORAGE_KEY,
      caches: ['localStorage']
    }
  });

function syncDocument(lng) {
  const meta = localeMeta(lng);
  document.documentElement.lang = meta.code;
  document.documentElement.dir = meta.dir;
}

// Keep <html lang/dir> in sync and persist the selection across refreshes.
i18n.on('languageChanged', (lng) => {
  const normalized = normalizeLocale(lng);
  syncDocument(normalized);
  try {
    localStorage.setItem(LOCALE_STORAGE_KEY, normalized);
  } catch {
    /* storage may be unavailable; ignore */
  }
});

syncDocument(i18n.language || DEFAULT_LOCALE);

export default i18n;
