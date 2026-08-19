import { useTranslation } from 'react-i18next';
import { SUPPORTED_LOCALES } from './localeRegistry';

function LanguageSelector({ className = '' }) {
  const { i18n } = useTranslation();

  return (
    <select
      className={`language-selector ${className}`}
      aria-label="Language"
      value={i18n.language?.split('-')[0] || 'en'}
      onChange={(e) => i18n.changeLanguage(e.target.value)}
    >
      {SUPPORTED_LOCALES.map((l) => (
        <option key={l.id} value={l.id}>{l.label}</option>
      ))}
    </select>
  );
}

export default LanguageSelector;
