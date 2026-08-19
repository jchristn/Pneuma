import { useTranslation } from 'react-i18next';
import Modal from './Modal';

/**
 * Custom confirmation dialog. Never uses window.confirm.
 */
function ConfirmModal({
  isOpen,
  onClose,
  onConfirm,
  title,
  message,
  entityName,
  warningMessage,
  confirmLabel,
  cancelLabel,
  variant = 'danger',
  isLoading = false
}) {
  const { t } = useTranslation();

  const icon = (() => {
    if (variant === 'warning' || variant === 'danger') {
      return (
        <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
          <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
          <line x1="12" y1="9" x2="12" y2="13" />
          <line x1="12" y1="17" x2="12.01" y2="17" />
        </svg>
      );
    }
    return (
      <svg width="44" height="44" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
        <circle cx="12" cy="12" r="10" />
        <line x1="12" y1="16" x2="12" y2="12" />
        <line x1="12" y1="8" x2="12.01" y2="8" />
      </svg>
    );
  })();

  return (
    <Modal isOpen={isOpen} onClose={onClose} title={title || t('common.confirm')} size="small">
      <div className="confirm-modal">
        <div className={`confirm-icon confirm-icon-${variant}`}>{icon}</div>
        {message && <p className="confirm-message">{message}</p>}
        {entityName && <p className="confirm-entity">{entityName}</p>}
        {warningMessage && <p className="confirm-warning">{warningMessage}</p>}
        <div className="confirm-actions">
          <button className="btn btn-secondary" onClick={onClose} disabled={isLoading}>
            {cancelLabel || t('common.cancel')}
          </button>
          <button
            className={variant === 'info' ? 'btn btn-primary' : 'btn btn-danger'}
            onClick={onConfirm}
            disabled={isLoading}
          >
            {isLoading ? t('common.loading') : confirmLabel || t('common.confirm')}
          </button>
        </div>
      </div>
    </Modal>
  );
}

export default ConfirmModal;
