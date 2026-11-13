import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../../context/AuthContext';
import Modal from '../Modal';
import ConfirmModal from '../ConfirmModal';
import AutoRefreshControl from '../AutoRefreshControl';
import CrawlPill from './CrawlPill';
import CopyableId from '../CopyableId';
import DataGrid from '../grid/DataGrid';
import SummaryCards from '../grid/SummaryCards';
import { formatDateTime, formatBytes, formatDurationMs } from '../../utils/format';
import { operationTone } from './crawlUtils';
import { fetchAllOperationObjects, OPERATION_CARD_ROWS, sameFilter, actionTone } from './crawlGrid';

const FAILED = '__failed__';
const RUNNING = ['Running', 'Ingesting'];

function outcomeKey(o) {
  if (o.succeeded === true) return 'ok';
  if (o.succeeded === false) return 'failed';
  return 'pending';
}

// One crawl operation: a one-line summary, the counts in two rows of cards (each filters the table), and every
// object the operation touched in a searchable, sortable, filterable grid that scrolls on its own. Held operations
// can have their deletions confirmed; finished ones can be deleted from the history.
function CrawlOperationModal({ operationId, onClose, onChanged }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [op, setOp] = useState(null);
  const [objects, setObjects] = useState([]);
  const [total, setTotal] = useState(0);
  const [capped, setCapped] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [facets, setFacets] = useState({});
  const [confirm, setConfirm] = useState(null);
  const [confirmBusy, setConfirmBusy] = useState(false);

  // A silent load (auto-refresh) keeps the current rows on screen instead of showing the loading state.
  const load = useCallback(async (opts) => {
    const silent = opts?.silent === true;
    if (!silent) setLoading(true);
    if (!silent) setError('');
    try {
      const [o, objs] = await Promise.all([
        apiClient.getCrawlOperation(operationId),
        fetchAllOperationObjects(apiClient, operationId)
      ]);
      setOp(o);
      setObjects(objs.items);
      setTotal(objs.total);
      setCapped(objs.capped);
      if (silent) setError('');
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
    } finally {
      setLoading(false);
    }
  }, [apiClient, operationId, t]);

  useEffect(() => { load(); }, [load]);
  const silentLoad = useCallback(() => load({ silent: true }), [load]);

  const outcomeLabel = useCallback((o) => {
    const k = outcomeKey(o);
    return k === 'ok' ? t('crawl.outcomeOk') : k === 'failed' ? t('crawl.outcomeFailed') : t('crawl.outcomePending');
  }, [t]);
  const failedLabel = t('crawl.outcomeFailed');

  const columns = useMemo(() => [
    { key: 'key', label: t('crawl.key'), value: (o) => o.externalKey, mono: true, fuzzy: true, width: '34%' },
    { key: 'action', label: t('crawl.action'), value: (o) => o.action, facet: true, width: '11%',
      render: (o) => <CrawlPill label={o.action} tone={actionTone(o.action)} /> },
    { key: 'outcome', label: t('crawl.outcome'), value: outcomeLabel, facet: true, width: '11%',
      render: (o) => {
        const k = outcomeKey(o);
        return <CrawlPill label={outcomeLabel(o)} tone={k === 'ok' ? 'success' : k === 'failed' ? 'danger' : 'info'} />;
      } },
    { key: 'detail', label: t('crawl.detail'), value: (o) => o.detail || '', width: '22%' },
    { key: 'link', label: t('crawl.link'), value: (o) => o.linkId || '', width: '22%', sortable: false,
      render: (o) => (o.linkId ? <CopyableId value={o.linkId} /> : '-') }
  ], [t, outcomeLabel]);

  // Cards set the grid's facets; the failed card means "outcome is Failed" in the current language.
  const toFacets = (filter) => {
    if (!filter) return {};
    const out = {};
    Object.entries(filter).forEach(([k, v]) => { out[k] = v.map((x) => (x === FAILED ? failedLabel : x)); });
    return out;
  };
  const cardRows = OPERATION_CARD_ROWS.map((row) => ({
    key: row.key,
    label: t(`crawl.cardRow.${row.key}`, row.key === 'scan' ? 'Found' : 'Changed'),
    cards: row.cards.map((c) => {
      const f = toFacets(c.filter);
      return {
        key: c.count,
        value: op ? (op[c.count] ?? 0) : '-',
        label: t(`crawl.count.${c.count}`),
        danger: c.danger,
        active: c.filter ? sameFilter(facets, f) : false,
        tip: c.filter ? t('crawl.cardFilterTip', 'Show only these rows (click again to show all)') : t('crawl.cardAllTip', 'Show all rows'),
        onClick: () => setFacets(c.filter && !sameFilter(facets, f) ? f : {})
      };
    })
  }));

  // Run a confirmed action; an error shows in the banner and the confirm dialog closes either way.
  const act = async (fn) => {
    setConfirmBusy(true);
    try { await fn(); } catch (err) { setError(err?.message || t('crawl.loadError')); } finally { setConfirmBusy(false); setConfirm(null); }
  };

  const running = op && RUNNING.includes(op.status);
  const duration = op?.finishedUtc && op?.startedUtc ? (new Date(op.finishedUtc) - new Date(op.startedUtc)) : null;

  const footer = (
    <>
      <button type="button" className="btn btn-danger" onClick={() => setConfirm('delete')} disabled={!op || running}
        title={running ? t('crawl.deleteOperationRunningTip', 'Cancel the operation before deleting it.') : t('crawl.deleteOperationTip', 'Remove this operation and its object records from the history. Ingested content is kept.')}>
        {t('crawl.deleteOperation', 'Delete operation')}
      </button>
      <span style={{ flex: 1 }} />
      {op?.status === 'Held' && (
        <button type="button" className="btn btn-danger" onClick={() => setConfirm('held')} title={t('crawl.confirmDeletionsTip')}>
          {t('crawl.confirmDeletions', { count: op.heldDeletions })}
        </button>
      )}
      <AutoRefreshControl storageKey="modal:crawl-operation" onRefresh={silentLoad}
        title={t('crawl.autoRefreshTip', 'Automatically reload this operation and its objects on the selected interval.')} />
      <button type="button" className="btn btn-secondary" onClick={() => load()} disabled={loading}>{loading ? t('common.loading') : t('common.refresh')}</button>
      <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
    </>
  );

  return (
    <Modal isOpen title={t('crawl.operationTitle')} size="data" onClose={onClose}>
      {error && <div className="error-banner">{error}</div>}
      {op && (
        <dl className="kv-grid op-details">
          <dt>{t('crawl.status')}</dt><dd><CrawlPill label={op.status} tone={operationTone(op.status)} /></dd>
          <dt>{t('crawl.plan')}</dt><dd><CopyableId value={op.planId} /></dd>
          <dt>{t('crawl.trigger')}</dt><dd>{op.trigger}</dd>
          <dt>{t('crawl.started')}</dt><dd>{formatDateTime(op.startedUtc)}</dd>
          <dt>{t('crawl.finished')}</dt><dd>{op.finishedUtc ? formatDateTime(op.finishedUtc) : '-'}</dd>
          {duration !== null && (<><dt>{t('crawl.duration', 'Took')}</dt><dd>{formatDurationMs(duration)}</dd></>)}
          <dt>{t('crawl.bytes')}</dt><dd>{formatBytes(op.bytesEnumerated || 0)}</dd>
          {op.error && (<><dt>{t('crawl.error')}</dt><dd className="op-error">{op.error}</dd></>)}
        </dl>
      )}
      {op && <SummaryCards rows={cardRows} />}
      <DataGrid rows={objects} columns={columns} rowKey={(o) => o.id} loading={loading}
        facets={facets} onFacetsChange={setFacets} exportName={`crawl-operation-${operationId}`} tableId="crawl-operation-objects"
        emptyText={t('crawl.noObjects')}
        note={capped ? t('crawl.objectsCapped', { shown: objects.length.toLocaleString(), total: total.toLocaleString(), defaultValue: 'Loaded the first {{shown}} of {{total}} objects.' }) : null} />
      <div className="form-actions op-actions">{footer}</div>
      <ConfirmModal isOpen={confirm === 'held'} title={t('crawl.confirmDeletionsTitle')} variant="danger"
        message={t('crawl.confirmDeletionsMessage', { count: op?.heldDeletions || 0 })}
        confirmLabel={t('crawl.confirmDeletionsButton')} isLoading={confirmBusy}
        onConfirm={() => act(async () => { await apiClient.confirmCrawlDeletions(operationId); await load(); onChanged?.(); })}
        onClose={() => setConfirm(null)} />
      <ConfirmModal isOpen={confirm === 'delete'} title={t('crawl.deleteOperationTitle', 'Delete crawl operation?')} variant="danger"
        message={t('crawl.deleteOperationMessage', 'This removes the operation and its per-object records from the history. Links and ingested content are not affected.')}
        confirmLabel={t('common.delete')} isLoading={confirmBusy}
        onConfirm={() => act(async () => { await apiClient.deleteCrawlOperation(operationId); onChanged?.(); onClose(); })}
        onClose={() => setConfirm(null)} />
    </Modal>
  );
}

export default CrawlOperationModal;
