import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import CrawlPill from './CrawlPill';
import ConnectivityResultView from './ConnectivityResultView';
import { formatBytes } from '../../utils/format';

const PREVIEW_COUNTS = ['enumerated', 'add', 'update', 'retry', 'unchanged', 'delete', 'missing', 'skip'];

// Runs a connectivity test (mode "test") or a preview (mode "preview") for a stored plan and shows the result.
// Neither changes anything.
function CrawlPreviewModal({ plan, mode, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [result, setResult] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const run = useCallback(async () => {
    setLoading(true);
    setError('');
    setResult(null);
    try {
      setResult(mode === 'test' ? await apiClient.testCrawlPlan(plan.id) : await apiClient.previewCrawlPlan(plan.id));
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, plan.id, mode, t]);

  useEffect(() => { run(); }, [run]);

  const footer = (
    <>
      <button type="button" className="btn btn-secondary" onClick={run} disabled={loading}>{t('common.retry', 'Retry')}</button>
      <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
    </>
  );

  return (
    <Modal isOpen title={mode === 'test' ? t('crawl.testTitle', { name: plan.name }) : t('crawl.previewTitle', { name: plan.name })} size="large" onClose={onClose}>
      {loading && <p className="confirm-text">{mode === 'test' ? t('crawl.testing') : t('crawl.previewing')}</p>}
      {error && <div className="form-error">{error}</div>}
      {mode === 'test' && result && <ConnectivityResultView result={result} />}
      {mode === 'preview' && result && (
        <>
          <p className="field-hint">{t('crawl.previewHint')}</p>
          <div className="crawl-counts">
            {PREVIEW_COUNTS.map((c) => (
              <div key={c} className="crawl-count">
                <div className="crawl-count-value">{result[c] ?? 0}</div>
                <div className="crawl-count-label">{t(`crawl.preview.${c}`)}</div>
              </div>
            ))}
          </div>
          <p className="field-hint">{t('crawl.bytes')}: {formatBytes(result.bytesEnumerated || 0)}</p>
          {result.deletionsHeld && <div className="form-error">{t('crawl.previewDeletionsHeld')}</div>}
          {Array.isArray(result.items) && result.items.length > 0 && (
            <table className="data-table">
              <thead><tr><th>{t('crawl.key')}</th><th>{t('crawl.action')}</th><th>{t('crawl.contentType')}</th><th>{t('crawl.size')}</th><th>{t('crawl.detail')}</th></tr></thead>
              <tbody>
                {result.items.map((i) => (
                  <tr key={`${i.action}-${i.key}`}>
                    <td className="wrap"><code>{i.key}</code></td>
                    <td><CrawlPill label={i.action} tone={i.action === 'Delete' ? 'danger' : i.action === 'Skip' ? 'neutral' : 'info'} /></td>
                    <td>{i.contentType || '-'}</td>
                    <td>{i.sizeBytes ? formatBytes(i.sizeBytes) : '-'}</td>
                    <td className="wrap">{i.detail || '-'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
          {result.truncated && <small className="field-hint">{t('crawl.truncated', { count: result.maxItems })}</small>}
        </>
      )}
      <div className="form-actions">{footer}</div>
    </Modal>
  );
}

export default CrawlPreviewModal;
