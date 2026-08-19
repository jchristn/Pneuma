import i18n from 'i18next';
import { initReactI18next } from 'react-i18next';
import LanguageDetector from 'i18next-browser-languagedetector';
import resources from './resources';
import { DEFAULT_LOCALE, STORAGE_KEY, SUPPORTED_LOCALES, normalizeLocale, getDirection } from './localeRegistry';

i18n
  .use(LanguageDetector)
  .use(initReactI18next)
  .init({
    resources,
    fallbackLng: DEFAULT_LOCALE,
    supportedLngs: SUPPORTED_LOCALES.map((l) => l.id),
    load: 'languageOnly',
    interpolation: { escapeValue: false },
    detection: {
      order: ['localStorage', 'navigator'],
      lookupLocalStorage: STORAGE_KEY,
      caches: ['localStorage']
    }
  });

function applyDocumentLocale(locale) {
  const normalized = normalizeLocale(locale);
  document.documentElement.lang = normalized;
  document.documentElement.dir = getDirection(normalized);
}

applyDocumentLocale(i18n.language);
i18n.on('languageChanged', applyDocumentLocale);

export default i18n;
