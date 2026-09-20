import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewer from '../components/JsonViewer';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import FollowLogsModal from '../components/FollowLogsModal';
import { getId } from '../components/ResourceView';
import { stageLabel } from '../components/IngestionTimeline';
import { formatDateTime } from '../i18n/formatters';

const STATUS_OPTIONS = ['', 'Queued', 'Running', 'Completed', 'Failed'];

function isFailed(job) {
  return String(job?.status || '').toLowerCase() === 'failed';
}

// A terminal job's log is a static record ("View Logs"); an in-flight one streams ("Follow Logs").
function isTerminal(job) {
  const s = String(job?.status || '').toLowerCase();
  return s === 'completed' || s === 'failed' || s === 'error' || s === 'cancelled' || s === 'canceled';
}

function IngestionQueueView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [status, setStatus] = useState('');
  const [subjectId, setSubjectId] = useState('');
  const [subjects, setSubjects] = useState([]);
  const [modal, setModal] = useState(null);
  // Brief, dismissible notice shown after a background deletion is dispatched (202 Accepted).
  const [notice, setNotice] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.listJobs(status || undefined, subjectId ? { subjectId } : null);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load jobs');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, status, subjectId]);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  // Open the consolidated Follow Logs / Job Detail modal (the shared FollowLogsModal fetches the log itself).
  const openLogs = useCallback((job) => setModal({ type: 'follow', item: job }), []);

  const { selectedItems, clear, selection } = useTableSelection(rows);
  const failedSelected = selectedItems.filter(isFailed);

  const restart = async (job) => {
    await apiClient.restartJob(getId(job));
    await load();
  };

  // Deletion is a background cascade server-side (202 Accepted): close the confirm immediately, surface a
  // background notice, dispatch the delete, then refresh (the row may briefly linger until the cascade lands).
  const remove = async (job) => {
    setModal(null);
    setNotice(t('jobs.deleteBackground'));
    await apiClient.deleteJob(getId(job));
    await load();
  };

  const bulkRestart = async () => {
    for (const job of failedSelected) {
      await apiClient.restartJob(getId(job));
    }
    setModal(null);
    clear();
    await load();
  };

  const bulkDelete = async () => {
    const ids = selectedItems.map((job) => getId(job));
    setModal(null);
    clear();
    setNotice(t('jobs.deleteBackground'));
    await apiClient.bulkDeleteJobs(ids);
    await load();
  };

  const bulkBar = (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={[
        { key: 'restart', label: t('jobs.restart'), disabled: failedSelected.length === 0,
          tip: failedSelected.length === 0 ? t('jobs.bulkRestartNone', 'Only failed jobs can be restarted.') : t('jobs.bulkRestartTip', { count: failedSelected.length, defaultValue: `Restart ${failedSelected.length} failed job(s).` }),
          onClick: () => setModal({ type: 'bulk-restart' }) },
        { key: 'delete', label: t('jobs.delete'), danger: true,
          tip: t('jobs.bulkDeleteTip', { count: selectedItems.length, defaultValue: `Delete ${selectedItems.length} job(s) and their downstream data.` }),
          onClick: () => setModal({ type: 'bulk-delete' }) }
      ]}
    />
  );

  const columns = [
    { key: 'id', label: 'Job ID', render: (r) => <CopyableId value={getId(r)} truncateLen={14} /> },
    { key: 'status', label: t('jobs.status'), render: (r) => {
      const ds = r.deletionStatus;
      if (ds === 'Pending' || ds === 'Deleting') return <span style={{ opacity: 0.6, fontStyle: 'italic' }}>{t('jobs.deletingStatus', 'deleting…')}</span>;
      if (ds === 'Failed') return <span style={{ opacity: 0.6, fontStyle: 'italic' }}>{t('jobs.deletionFailed', 'deletion failed')}</span>;
      return <StatusPill label={r.status} tone={toneForStatus(r.status)} />;
    } },
    { key: 'stage', label: 'Stage', render: (r) => stageLabel(r.stage || r.currentStage) },
    { key: 'linkId', label: 'Link', render: (r) => <CopyableId value={r.linkId} truncateLen={12} /> },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc) },
    { key: 'updatedUtc', label: 'Updated', render: (r) => formatDateTime(r.updatedUtc || r.completedUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (job) => {
      const deleting = job.deletionStatus === 'Pending' || job.deletionStatus === 'Deleting';
      return (
      <ActionMenu items={[
        { key: 'follow', label: isTerminal(job) ? t('jobs.viewLogs', 'View Logs') : t('jobs.followLogs', 'Follow Logs'), tip: 'Open this job’s stage-by-stage log (streams live until it finishes).', onClick: () => openLogs(job) },
        { key: 'json', label: t('common.viewJson'), tip: 'Inspect the raw job record returned by the API.', onClick: () => setModal({ type: 'json', item: job }) },
        { key: 'restart', label: t('jobs.restart'), tip: 'Re-run this failed job from the beginning with the same settings.', hidden: !isFailed(job) || deleting, onClick: () => setModal({ type: 'restart', item: job }) },
        { key: 'delete', label: t('jobs.delete'), tip: 'Delete this job and cascade-remove its graph nodes, indexed chunks, and logs.', hidden: deleting, danger: true, onClick: () => setModal({ type: 'delete', item: job }) }
      ]} />
      );
    } }
  ];

  return (
    <div>
      <PageHeader title={t('jobs.title')} subtitle={t('jobs.subtitle')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="job-status" className="has-tip" title="Filter the queue to jobs in a particular state (queued, processing, failed…). Choose all statuses to clear the filter.">{t('jobs.status')}</label>
          <select id="job-status" value={status} onChange={(e) => setStatus(e.target.value)}
            title="Filter the queue to jobs in a particular state (queued, processing, failed…). Choose all statuses to clear the filter.">
            {STATUS_OPTIONS.map((s) => <option key={s} value={s}>{s || t('jobs.allStatuses')}</option>)}
          </select>
        </div>
        <div className="field">
          <label htmlFor="job-subject" className="has-tip" title="Filter the queue to a single subject. Choose all subjects to clear the filter.">{t('jobs.subject', 'Subject')}</label>
          <select id="job-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)}
            title="Filter the queue to a single subject. Choose all subjects to clear the filter.">
            <option value="">{t('jobs.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={openLogs}
        selection={selection} bulkBar={bulkBar} />

      {modal?.type === 'follow' && (
        <FollowLogsModal job={modal.item} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'json' && <JsonViewer data={modal.item} onClose={() => setModal(null)} />}
      {modal?.type === 'restart' && (
        <ConfirmModal title={t('jobs.restart')} message={t('jobs.restartConfirm')} danger={false}
          confirmLabel={t('common.restart')} onConfirm={() => restart(modal.item)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('jobs.delete')} message={t('jobs.deleteConfirm')} danger
          confirmLabel={t('common.delete')} onConfirm={() => remove(modal.item)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'bulk-restart' && (
        <ConfirmModal title={t('jobs.restart')}
          message={t('jobs.bulkRestartConfirm', { count: failedSelected.length, defaultValue: `Restart ${failedSelected.length} failed job(s) from the beginning?` })}
          confirmLabel={t('common.restart')} onConfirm={bulkRestart} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'bulk-delete' && (
        <ConfirmModal title={t('jobs.delete')} danger
          message={t('jobs.bulkDeleteConfirm', { count: selectedItems.length, defaultValue: `Delete ${selectedItems.length} job(s)? This cascade-removes their graph nodes, indexed chunks, and logs, and cannot be undone.` })}
          confirmLabel={t('common.delete')} onConfirm={bulkDelete} onClose={() => setModal(null)} />
      )}
      {notice && (
        <Modal
          title={t('common.notice')}
          size="sm"
          onClose={() => setNotice('')}
          footer={<button type="button" className="button-primary" onClick={() => setNotice('')}>{t('common.close')}</button>}
        >
          <p className="confirm-text">{notice}</p>
        </Modal>
      )}
    </div>
  );
}

export default IngestionQueueView;
