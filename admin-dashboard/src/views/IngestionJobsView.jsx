import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ActionMenu from '../components/ActionMenu';
import ConfirmModal from '../components/ConfirmModal';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import FollowLogsModal from '../components/FollowLogsModal';
import JobPerformanceModal from '../components/JobPerformanceModal';
import { stageLabel } from '../components/IngestionTimeline';
import { getId } from '../components/ResourceView';
import { formatDateTime } from '../i18n/formatters';

const STATUS_OPTIONS = ['', 'Queued', 'Processing', 'Completed', 'Failed', 'Cancelled'];

function isStoppable(job) {
  const s = String(job?.status || '').toLowerCase();
  return s === 'queued' || s === 'processing';
}

// Default table ordering: jobs that need attention or are in flight (anything NOT completed or queued)
// sort to the top; completed and queued jobs sink to the bottom. Lower rank sorts first.
function jobStateRank(status) {
  const s = String(status || '').toLowerCase();
  if (s === 'completed') return 90;
  if (s === 'queued') return 80;
  if (s === 'failed' || s === 'error') return 0;
  if (s === 'processing') return 10;
  if (s === 'running') return 11;
  if (s === 'cancelled' || s === 'canceled') return 20;
  return 15; // any other in-flight/unknown state still sorts above queued/completed
}

function IngestionJobsView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [status, setStatus] = useState('');
  const [subjectId, setSubjectId] = useState('');
  const [subjects, setSubjects] = useState([]);
  const [modal, setModal] = useState(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const query = { maxResults: 1000, order: 'desc', ...(subjectId ? { subjectId } : {}) };
      const resp = await apiClient.listJobs(status || undefined, query);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load ingestion jobs');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, status, subjectId]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  // Sort by state (attention/in-flight first), then most-recently-updated within a state group.
  const sortedRows = useMemo(() => {
    return [...rows].sort((a, b) => {
      const r = jobStateRank(a.status) - jobStateRank(b.status);
      if (r !== 0) return r;
      const at = a.lastUpdateUtc || a.updatedUtc || a.completedUtc || a.createdUtc || '';
      const bt = b.lastUpdateUtc || b.updatedUtc || b.completedUtc || b.createdUtc || '';
      return String(bt).localeCompare(String(at));
    });
  }, [rows]);

  const { selectedItems, clear, selection } = useTableSelection(rows);
  const stoppableSelected = selectedItems.filter(isStoppable);

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

  const bulkStop = async () => {
    for (const job of stoppableSelected) {
      await apiClient.stopJob(getId(job));
    }
    setModal(null);
    clear();
    await load();
  };

  const bulkDelete = async () => {
    for (const job of selectedItems) {
      await apiClient.deleteJob(getId(job));
    }
    setModal(null);
    clear();
    await load();
  };

  const bulkBar = (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={[
        { key: 'stop', label: t('jobs.stop'), danger: true, disabled: stoppableSelected.length === 0,
          tip: stoppableSelected.length === 0 ? t('jobs.bulkStopNone', 'Only queued or in-progress jobs can be stopped.') : t('jobs.bulkStopTip', { count: stoppableSelected.length, defaultValue: `Stop ${stoppableSelected.length} in-progress job(s).` }),
          onClick: () => setModal({ type: 'bulk-stop' }) },
        { key: 'delete', label: t('jobs.delete'), danger: true,
          tip: t('jobs.bulkDeleteTip', { count: selectedItems.length, defaultValue: `Delete ${selectedItems.length} job(s) and their downstream data.` }),
          onClick: () => setModal({ type: 'bulk-delete' }) }
      ]}
    />
  );

  const columns = [
    { key: 'sourceUrl', label: t('jobs.source'), render: (r) => (
      <span title={r.sourceUrl || r.linkId} style={{ display: 'inline-block', maxWidth: '360px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap', verticalAlign: 'bottom' }}>
        {r.sourceUrl || <CopyableId value={r.linkId} truncateLen={12} />}
      </span>
    ) },
    { key: 'status', label: t('jobs.status'), render: (r) => <StatusPill label={r.status} tone={toneForStatus(r.status)} /> },
    { key: 'stage', label: t('jobs.stage'), render: (r) => stageLabel(r.stage || r.currentStage) },
    { key: 'createdUtc', label: t('jobs.created'), render: (r) => formatDateTime(r.createdUtc) },
    { key: 'updatedUtc', label: t('jobs.updated'), render: (r) => formatDateTime(r.lastUpdateUtc || r.updatedUtc || r.completedUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (job) => (
      <ActionMenu items={[
        { key: 'follow', label: t('jobs.followLogs'), tip: 'Watch this job’s stage log live, auto-refreshing until it finishes.', onClick: () => setModal({ type: 'follow', item: job }) },
        { key: 'performance', label: t('jobs.viewPerformance', 'View Performance'), tip: 'Visualize where this job spent time — a bar per stage sized by its duration, with discrete timings.', onClick: () => setModal({ type: 'performance', item: job }) },
        { key: 'stop', label: t('jobs.stop'), tip: 'Cancel this in-progress job. Already-completed stages are kept.', hidden: !isStoppable(job), danger: true, onClick: () => setModal({ type: 'stop', item: job }) },
        { key: 'delete', label: t('jobs.delete'), tip: 'Delete this job and cascade-remove its graph nodes, indexed chunks, and logs.', danger: true, onClick: () => setModal({ type: 'delete', item: job }) }
      ]} />
    ) }
  ];

  return (
    <div>
      <PageHeader title={t('jobs.jobsTitle')} subtitle={t('jobs.jobsSubtitle')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="ingestion-job-status" className="has-tip" title="Filter the full job history by state. Choose all statuses to clear the filter.">{t('jobs.status')}</label>
          <select id="ingestion-job-status" value={status} onChange={(e) => setStatus(e.target.value)}
            title="Filter the full job history by state. Choose all statuses to clear the filter.">
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s || t('jobs.allStatuses')}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="ingestion-job-subject" className="has-tip" title="Filter the job history to a single subject. Choose all subjects to clear the filter.">{t('jobs.subject', 'Subject')}</label>
          <select id="ingestion-job-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}
            title="Filter the job history to a single subject. Choose all subjects to clear the filter.">
            <option value="">{t('jobs.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={sortedRows} loading={loading} onRefresh={load}
        onRowClick={(job) => setModal({ type: 'follow', item: job })}
        selection={selection} bulkBar={bulkBar} />

      {modal?.type === 'follow' && (
        <FollowLogsModal job={modal.item} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'performance' && (
        <JobPerformanceModal job={modal.item} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'stop' && (
        <ConfirmModal title={t('jobs.stop')} message={t('jobs.stopConfirm')} danger
          confirmLabel={t('jobs.stop')} onConfirm={() => stop(modal.item)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('jobs.delete')} message={t('jobs.deleteConfirm')} danger
          confirmLabel={t('common.delete')} onConfirm={() => remove(modal.item)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'bulk-stop' && (
        <ConfirmModal title={t('jobs.stop')} danger
          message={t('jobs.bulkStopConfirm', { count: stoppableSelected.length, defaultValue: `Stop ${stoppableSelected.length} in-progress job(s)? Already-completed stages are kept.` })}
          confirmLabel={t('jobs.stop')} onConfirm={bulkStop} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'bulk-delete' && (
        <ConfirmModal title={t('jobs.delete')} danger
          message={t('jobs.bulkDeleteConfirm', { count: selectedItems.length, defaultValue: `Delete ${selectedItems.length} job(s)? This cascade-removes their graph nodes, indexed chunks, and logs, and cannot be undone.` })}
          confirmLabel={t('common.delete')} onConfirm={bulkDelete} onClose={() => setModal(null)} />
      )}
    </div>
  );
}

export default IngestionJobsView;
