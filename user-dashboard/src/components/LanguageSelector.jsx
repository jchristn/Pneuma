import { useTranslation } from 'react-i18next';
import { SUPPORTED_LOCALES } from '../i18n/localeRegistry.js';
import Icon from './Icon.jsx';

export default function LanguageSelector() {
  const { i18n, t } = useTranslation();
  const current = SUPPORTED_LOCALES.find((l) => l.code === i18n.language) ? i18n.language : 'en';

  return (
    <label className="language-selector" title={t('language.label')}>
      <Icon name="globe" size={16} />
      <span className="sr-only">{t('language.label')}</span>
      <select
        aria-label={t('language.label')}
        value={current}
        onChange={(event) => i18n.changeLanguage(event.target.value)}
      >
        {SUPPORTED_LOCALES.map((locale) => (
          <option key={locale.code} value={locale.code}>
            {locale.nativeLabel}
          </option>
        ))}
      </select>
    </label>
  );
}
