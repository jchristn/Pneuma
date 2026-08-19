import { useTranslation } from 'react-i18next';
import { SUPPORTED_LOCALES, normalizeLocale } from '../i18n/localeRegistry';

/**
 * Shared locale selector. Persists via i18next language detector (localStorage).
 */
function LanguageSelector({ className = '' }) {
  const { i18n } = useTranslation();
  const current = normalizeLocale(i18n.language);

  return (
    <select
      className={`lang-select ${className}`}
      value={current}
      onChange={(e) => i18n.changeLanguage(e.target.value)}
      title="Language"
      aria-label="Language"
    >
      {SUPPORTED_LOCALES.map((locale) => (
        <option key={locale.id} value={locale.id}>
          {locale.label}
        </option>
      ))}
    </select>
  );
}

export default LanguageSelector;
