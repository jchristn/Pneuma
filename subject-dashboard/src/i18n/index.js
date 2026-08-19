import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import resources from './resources';
import {
  STORAGE_KEY,
  DEFAULT_LOCALE,
  SUPPORTED_LOCALES,
  normalizeLocale,
  getDirection
} from './localeRegistry';
import { setFormatterLocale } from '../utils/format';

function applyDocumentLocale(locale) {
  const normalized = normalizeLocale(locale);
  document.documentElement.lang = normalized;
  document.documentElement.dir = getDirection(normalized);
  setFormatterLocale(normalized);
}

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init(
    {
      resources,
      fallbackLng: DEFAULT_LOCALE,
      supportedLngs: SUPPORTED_LOCALES.map((l) => l.id),
      load: 'languageOnly',
      detection: {
        order: ['localStorage', 'navigator'],
        lookupLocalStorage: STORAGE_KEY,
        caches: ['localStorage']
      },
      interpolation: { escapeValue: false }
    },
    () => {
      applyDocumentLocale(i18n.language);
    }
  );

i18n.on('languageChanged', (lng) => {
  applyDocumentLocale(lng);
});

export default i18n;
