import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ConfirmModal from '../components/ConfirmModal';
import ErrorBanner from '../components/ErrorBanner';
import StatusPill from '../components/StatusPill';
import CrawlOperationModal from '../components/crawl/CrawlOperationModal';
import { operationTone, OPERATION_STATUSES } from '../components/crawl/crawlUtils';
import { formatDateTime } from '../i18n/formatters';

// Crawl operations across plans: filter by plan and status; a row opens the operation with its per-object results.
function CrawlOperationsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [plans, setPlans] = useState([]);
  const [planId, setPlanId] = useState('');
  const [status, setStatus] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [openId, setOpenId] = useState(null);
  const [toDelete, setToDelete] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      setRows(normalizeList(await apiClient.listCrawlOperations(planId || null, status || null)).items);
    } catch (err) {
      setError(err?.message || t('crawl.loadError'));
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, planId, status, t]);

  useEffect(() => { load(); }, [load]);
  useEffect(() => {
    apiClient.listCrawlPlans().then((r) => setPlans(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  const planName = useMemo(() => {
    const map = {};
    for (const p of plans) map[p.id] = p.name;
    return map;
  }, [plans]);

  const { selectedItems, clear, selection } = useTableSelection(rows);
  const isRunning = (op) => op.status === 'Running' || op.status === 'Ingesting';

  // Delete finished operations one by one; running ones are refused by the server (409) and skipped here.
  const removeOperations = async (ops) => {
    const failures = [];
    for (const op of ops) {
      try { await apiClient.deleteCrawlOperation(op.id); } catch (err) { failures.push(err?.message || op.id); }
    }
    clear();
    await load();
    if (failures.length) throw new Error(failures.join(' '));
  };

  const bulkBar = (
    <BulkActionBar count={selectedItems.length} onClear={clear} actions={[
      { key: 'delete', label: t('common.delete'), danger: true,
        tip: t('crawl.bulkDeleteOperationsTip', 'Delete the selected operations from the history (running ones cannot be deleted).'),
        disabled: selectedItems.some(isRunning),
        onClick: () => setToDelete(selectedItems) }
    ]} />
  );

  const columns = [
    { key: 'startedUtc', label: t('crawl.started'), render: (r) => formatDateTime(r.startedUtc) },
    { key: 'planId', label: t('crawl.plan'), render: (r) => planName[r.planId] || r.planId },
    { key: 'status', label: t('crawl.status'), render: (r) => <StatusPill label={r.status} tone={operationTone(r.status)} /> },
    { key: 'trigger', defaultHidden: true, label: t('crawl.trigger') },
    { key: 'enumerated', defaultHidden: true, label: t('crawl.count.enumerated') },
    { key: 'added', label: t('crawl.count.added') },
    { key: 'updated', label: t('crawl.count.updated') },
    { key: 'deleted', label: t('crawl.count.deleted') },
    { key: 'failed', label: t('crawl.count.failed') },
    { key: 'finishedUtc', defaultHidden: true, label: t('crawl.finished'), render: (r) => (r.finishedUtc ? formatDateTime(r.finishedUtc) : '-') },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (op) => (
      <ActionMenu items={[
        { key: 'view', label: t('crawl.view'), tip: t('crawl.viewOperationTip'), onClick: () => setOpenId(op.id) },
        { key: 'delete', label: t('common.delete'), danger: true, hidden: isRunning(op),
          tip: t('crawl.deleteOperationTip', 'Remove this operation and its object records from the history. Ingested content is kept.'),
          onClick: () => setToDelete([op]) }
      ]} />
    ) }
  ];

  return (
    <div>
      <PageHeader title={t('crawl.operationsTitle')} subtitle={t('crawl.operationsSubtitle')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="crawl-ops-plan">{t('crawl.plan')}</label>
          <select id="crawl-ops-plan" value={planId} onChange={(e) => setPlanId(e.target.value)}>
            <option value="">{t('crawl.allPlans')}</option>
            {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="crawl-ops-status">{t('crawl.status')}</label>
          <select id="crawl-ops-status" value={status} onChange={(e) => setStatus(e.target.value)}>
            <option value="">{t('crawl.allStatuses')}</option>
            {OPERATION_STATUSES.map((s) => <option key={s} value={s}>{s}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError('')} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={(r) => setOpenId(r.id)}
        selection={selection} bulkBar={bulkBar} emptyMessage={t('crawl.noOperations')} />
      {toDelete && (
        <ConfirmModal danger title={t('crawl.deleteOperationsTitle', { count: toDelete.length, defaultValue: 'Delete {{count}} crawl operation(s)?' })}
          message={t('crawl.deleteOperationMessage', 'This removes the operation and its per-object records from the history. Links and ingested content are not affected.')}
          confirmLabel={t('common.delete')} onConfirm={() => removeOperations(toDelete)} onClose={() => setToDelete(null)} />
      )}
      {openId && <CrawlOperationModal operationId={openId} onClose={() => setOpenId(null)} onChanged={load} />}
    </div>
  );
}

export default CrawlOperationsView;
