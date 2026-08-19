import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';

/**
 * Themed 404 page. Renders standalone for unknown top-level routes and inside the
 * dashboard shell for unknown sections.
 */
function NotFound() {
  const navigate = useNavigate();
  const { t } = useTranslation();
  return (
    <div className="not-found">
      <div className="not-found-code">{t('notFound.code', '404')}</div>
      <h1 className="not-found-title">{t('notFound.title', 'Page not found')}</h1>
      <p className="not-found-message">
        {t('notFound.message', 'The page you’re looking for doesn’t exist or may have moved.')}
      </p>
      <button type="button" className="btn btn-primary" onClick={() => navigate('/dashboard/home')}>
        {t('notFound.back', 'Back to dashboard')}
      </button>
    </div>
  );
}

export default NotFound;
