import { useTranslation } from 'react-i18next';
import { useTheme } from '../context/ThemeContext.jsx';
import Icon from './Icon.jsx';

export default function ThemeToggle() {
  const { theme, toggle } = useTheme();
  const { t } = useTranslation();
  const label = theme === 'dark' ? t('theme.toggleToLight') : t('theme.toggleToDark');

  return (
    <button type="button" className="icon-button" onClick={toggle} aria-label={label} title={label}>
      <Icon name={theme === 'dark' ? 'sun' : 'moon'} size={18} />
    </button>
  );
}
