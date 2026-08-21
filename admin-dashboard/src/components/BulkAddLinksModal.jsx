import { useState, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import Modal from './Modal';

// Parse a textarea of URLs (one per line): trim each line and drop blanks.
function parseUrls(text) {
  return String(text || '')
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/**
 * Modal for enqueuing ingestion of many URLs at once for a single subject.
 * The subject owns the embedding/inference models and collection, so this needs only a subject and URLs.
 */
function BulkAddLinksModal({ subjectOptions, onClose, onCreated }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [subjectId, setSubjectId] = useState('');
  const [urlsText, setUrlsText] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');

  const urls = useMemo(() => parseUrls(urlsText), [urlsText]);
  const canSubmit = !!subjectId && urls.length > 0 && !busy;

  const submit = async (e) => {
    e.preventDefault();
    if (!canSubmit) return;
    setBusy(true);
    setError('');
    try {
      const resp = await apiClient.bulkSubmitLinks(subjectId, { urls });
      const created = resp?.created ?? resp?.Created ?? (resp?.links || resp?.Links || []).length;
      onCreated(created);
    } catch (err) {
      setError(err?.message || 'Bulk submit failed');
      setBusy(false);
    }
  };

  return (
    <Modal
      title={t('links.addMultiple')}
      size="lg"
      onClose={busy ? () => {} : onClose}
    >
      <form onSubmit={submit}>
        <div className="form-grid">
          <div className="field">
            <label htmlFor="bulk-subject">{t('links.subject')}</label>
            <select id="bulk-subject" value={subjectId} required onChange={(e) => setSubjectId(e.target.value)}>
              <option value="">{t('links.selectSubject')}</option>
              {subjectOptions.map((o) => (
                <option key={o.value} value={o.value}>{o.label}</option>
              ))}
            </select>
          </div>
          <div className="field">
            <label htmlFor="bulk-urls">{t('links.urls')}</label>
            <textarea id="bulk-urls" rows={10} value={urlsText} required
              placeholder={t('links.urlsPlaceholder')}
              onChange={(e) => setUrlsText(e.target.value)} />
            <div className="field-hint" style={{ marginTop: '0.35rem', fontSize: 'var(--font-size-xs)', color: 'var(--color-text-secondary)' }}>
              {t('links.subjectModelsHint', 'These links are ingested with the subject’s configured embedding and inference models and its collection.')}
            </div>
          </div>
        </div>
        {error && <div className="error-message" style={{ marginTop: '1rem' }}>{error}</div>}
        <div className="modal-footer" style={{ padding: '1rem 0 0', borderTop: 'none' }}>
          <button type="button" className="button-secondary" onClick={onClose} disabled={busy}>{t('common.cancel')}</button>
          <button type="submit" className="button-primary" disabled={!canSubmit}>{busy ? t('common.loading') : t('common.create')}</button>
        </div>
      </form>
    </Modal>
  );
}

export default BulkAddLinksModal;
