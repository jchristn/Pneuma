import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import ConfirmModal from '../components/ConfirmModal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import FollowLogsModal from '../components/FollowLogsModal';
import { getId } from '../components/ResourceView';
import { formatDateTime } from '../i18n/formatters';

const STATUS_OPTIONS = ['', 'Queued', 'Processing', 'Completed', 'Failed', 'Cancelled'];

function isStoppable(job) {
  const s = String(job?.status || '').toLowerCase();
  return s === 'queued' || s === 'processing';
}

function IngestionJobsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [status, setStatus] = useState('');
  const [modal, setModal] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.listJobs(status || undefined, { maxResults: 1000, order: 'desc' });
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load ingestion jobs');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, status]);

  useEffect(() => { load(); }, [load]);

  const stop = async (job) => {
    await apiClient.stopJob(getId(job));
    setModal(null);
    await load();
  };

  const remove = async (job) => {
    await apiClient.deleteJob(getId(job));
    setModal(null);
    await load();
  };

  const columns = [
    { key: 'sourceUrl', label: t('jobs.source'), render: (r) => (
      <span title={r.sourceUrl || r.linkId} style={{ display: 'inline-block', maxWidth: '360px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', verticalAlign: 'bottom' }}>
        {r.sourceUrl || <CopyableId value={r.linkId} truncateLen={12} />}
      </span>
    ) },
    { key: 'status', label: t('jobs.status'), render: (r) => <StatusPill label={r.status} tone={toneForStatus(r.status)} /> },
    { key: 'stage', label: t('jobs.stage'), render: (r) => r.stage || r.currentStage || '—' },
    { key: 'createdUtc', label: t('jobs.created'), render: (r) => formatDateTime(r.createdUtc) },
    { key: 'updatedUtc', label: t('jobs.updated'), render: (r) => formatDateTime(r.lastUpdateUtc || r.updatedUtc || r.completedUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (job) => (
      <ActionMenu items={[
        { key: 'follow', label: t('jobs.followLogs'), onClick: () => setModal({ type: 'follow', item: job }) },
        { key: 'stop', label: t('jobs.stop'), hidden: !isStoppable(job), danger: true, onClick: () => setModal({ type: 'stop', item: job }) },
        { key: 'delete', label: t('jobs.delete'), danger: true, onClick: () => setModal({ type: 'delete', item: job }) }
      ]} />
    ) }
  ];

  return (
    <div>
      <PageHeader title={t('jobs.jobsTitle')} subtitle={t('jobs.jobsSubtitle')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="ingestion-job-status">{t('jobs.status')}</label>
          <select id="ingestion-job-status" value={status} onChange={(e) => setStatus(e.target.value)}>
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s || t('jobs.allStatuses')}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load}
        onRowClick={(job) => setModal({ type: 'follow', item: job })} />

      {modal?.type === 'follow' && (
        <FollowLogsModal job={modal.item} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'stop' && (
        <ConfirmModal title={t('jobs.stop')} message={t('jobs.stopConfirm')} danger
          confirmLabel={t('jobs.stop')} onConfirm={() => stop(modal.item)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('jobs.delete')} message={t('jobs.deleteConfirm')} danger
          confirmLabel={t('common.delete')} onConfirm={() => remove(modal.item)} onClose={() => setModal(null)} />
      )}
    </div>
  );
}

export default IngestionJobsView;
