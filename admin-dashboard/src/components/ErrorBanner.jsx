import { useTranslation } from 'react-i18next';

function ErrorBanner({ message, onRetry, onDismiss }) {
  const { t } = useTranslation();
  if (!message) return null;
  return (
    <div className="error-banner" role="alert">
      <span>{message}</span>
      <span style={{ display: 'flex', gap: '0.5rem' }}>
        {onRetry && <button type="button" className="button-secondary" onClick={onRetry}>{t('common.retry')}</button>}
        {onDismiss && <button type="button" className="icon-button" onClick={onDismiss} aria-label={t('common.close')}>✕</button>}
      </span>
    </div>
  );
}

export default ErrorBanner;
