import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import Modal from './Modal';

function ConfirmModal({ title, message, confirmLabel, danger = true, onConfirm, onClose }) {
  const { t } = useTranslation();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const handleConfirm = async () => {
    setBusy(true);
    setError('');
    try {
      await onConfirm();
      onClose();
    } catch (err) {
      setError(err?.message || 'Operation failed');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={title || t('common.confirm')}
      size="sm"
      onClose={busy ? () => {} : onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="button" className={danger ? 'button-danger' : 'button-primary'} onClick={handleConfirm} disabled={busy}>
            {busy ? t('common.loading') : (confirmLabel || t('common.confirm'))}
          </button>
        </>
      )}
    >
      <p className="confirm-text">{message}</p>
      {error && <div className="error-message" style={{ marginTop: '0.75rem' }}>{error}</div>}
    </Modal>
  );
}

export default ConfirmModal;
