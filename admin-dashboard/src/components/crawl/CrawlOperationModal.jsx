import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import { normalizeList } from '../../utils/api';
import Modal from '../Modal';
import ConfirmModal from '../ConfirmModal';
import StatusPill from '../StatusPill';
import CopyableId from '../CopyableId';
import ErrorBanner from '../ErrorBanner';
import { formatDateTime, formatBytes } from '../../i18n/formatters';
import { operationTone, ACTIONS } from './crawlUtils';

const COUNTS = ['enumerated', 'added', 'updated', 'retried', 'unchanged', 'deleted', 'missing', 'skipped', 'failed'];

function actionTone(action) {
  if (action === 'Add' || action === 'Update' || action === 'Retry') return 'info';
  if (action === 'Delete' || action === 'Fail') return 'danger';
  return 'neutral';
}

function outcomeLabel(o, t) {
  if (o.succeeded === true) return <StatusPill label={t('crawl.outcomeOk')} tone="success" />;
  if (o.succeeded === false) return <StatusPill label={t('crawl.outcomeFailed')} tone="danger" />;
  return <StatusPill label={t('crawl.outcomePending')} tone="info" />;
}

// One crawl operation: counts, timing, the per-object results (filterable by action), and, when the operation is
// Held, the button that confirms its deletions.
function CrawlOperationModal({ operationId, onClose, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [op, setOp] = useState(null);
  const [objects, setObjects] = useState([]);
  const [action, setAction] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [confirming, setConfirming] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [o, objs] = await Promise.all([
        apiClient.getCrawlOperation(operationId),
        apiClient.listCrawlOperationObjects(operationId, action || null)
      ]);
      setOp(o);
      setObjects(normalizeList(objs).items);
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, operationId, action, t]);

  useEffect(() => { load(); }, [load]);

  const confirmDeletions = async () => {
    await apiClient.confirmCrawlDeletions(operationId);
    await load();
    onChanged?.();
  };

  const footer = (
    <>
      {op?.status === 'Held' && (
        <button type="button" className="button-danger" onClick={() => setConfirming(true)} title={t('crawl.confirmDeletionsTip')}>
          {t('crawl.confirmDeletions', { count: op.heldDeletions })}
        </button>
      )}
      <button type="button" className="button-secondary" onClick={load} disabled={loading}>{t('common.refresh')}</button>
      <button type="button" className="button-primary" onClick={onClose}>{t('common.close')}</button>
    </>
  );

  return (
    <Modal title={t('crawl.operationTitle')} subtitle={operationId} size="xl" onClose={onClose} footer={footer}>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError('')} />}
      {op && (
        <>
          <dl className="kv-grid" style={{ marginBottom: '1rem' }}>
            <dt>{t('crawl.status')}</dt><dd><StatusPill label={op.status} tone={operationTone(op.status)} /></dd>
            <dt>{t('crawl.plan')}</dt><dd><CopyableId value={op.planId} truncateLen={16} /></dd>
            <dt>{t('crawl.trigger')}</dt><dd>{op.trigger}</dd>
            <dt>{t('crawl.started')}</dt><dd>{formatDateTime(op.startedUtc)}</dd>
            <dt>{t('crawl.finished')}</dt><dd>{op.finishedUtc ? formatDateTime(op.finishedUtc) : '-'}</dd>
            <dt>{t('crawl.bytes')}</dt><dd>{formatBytes(op.bytesEnumerated || 0)}</dd>
            {op.error && (<><dt>{t('crawl.error')}</dt><dd className="wrap">{op.error}</dd></>)}
          </dl>
          <div className="crawl-counts">
            {COUNTS.map((c) => (
              <div key={c} className="crawl-count">
                <div className="crawl-count-value">{op[c] ?? 0}</div>
                <div className="crawl-count-label">{t(`crawl.count.${c}`)}</div>
              </div>
            ))}
          </div>
        </>
      )}
      <div className="filter-bar" style={{ marginTop: '1rem' }}>
        <div className="field">
          <label htmlFor="crawl-op-action">{t('crawl.action')}</label>
          <select id="crawl-op-action" value={action} onChange={(e) => setAction(e.target.value)}>
            <option value="">{t('crawl.allActions')}</option>
            {ACTIONS.map((a) => <option key={a} value={a}>{a}</option>)}
          </select>
        </div>
      </div>
      {!loading && objects.length === 0 && <div className="empty-message">{t('crawl.noObjects')}</div>}
      {objects.length > 0 && (
        <table className="data-table">
          <thead>
            <tr><th>{t('crawl.key')}</th><th>{t('crawl.action')}</th><th>{t('crawl.outcome')}</th><th>{t('crawl.detail')}</th><th>{t('crawl.link')}</th></tr>
          </thead>
          <tbody>
            {objects.slice(0, 500).map((o) => (
              <tr key={o.id}>
                <td className="wrap"><code>{o.externalKey}</code></td>
                <td><StatusPill label={o.action} tone={actionTone(o.action)} /></td>
                <td>{outcomeLabel(o, t)}</td>
                <td className="wrap">{o.detail || '-'}</td>
                <td>{o.linkId ? <CopyableId value={o.linkId} truncateLen={12} /> : '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
      {objects.length > 500 && <small className="field-hint">{t('crawl.truncated', { count: 500 })}</small>}
      {confirming && (
        <ConfirmModal title={t('crawl.confirmDeletionsTitle')} danger
          message={t('crawl.confirmDeletionsMessage', { count: op?.heldDeletions || 0 })}
          confirmLabel={t('crawl.confirmDeletionsButton')} onConfirm={confirmDeletions} onClose={() => setConfirming(false)} />
      )}
    </Modal>
  );
}

export default CrawlOperationModal;
