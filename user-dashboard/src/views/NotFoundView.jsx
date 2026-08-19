import { Link } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

/**
 * Themed 404 page shown for any unmatched route within the app shell.
 */
export default function NotFoundView() {
  const { t } = useTranslation();
  return (
    <div className="not-found">
      <div className="not-found-code">{t('notFound.code')}</div>
      <h1 className="not-found-title">{t('notFound.title')}</h1>
      <p className="not-found-message">{t('notFound.message')}</p>
      <Link to="/" className="button-primary">{t('notFound.backToSearch')}</Link>
    </div>
  );
}
