import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import ActionMenu from '../components/ActionMenu';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import JsonViewer from '../components/JsonViewer';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import CopyButton from '../components/CopyButton';
import StatusPill, { toneForStatus } from '../components/StatusPill';
import { getId } from '../components/ResourceView';
import { formatDateTime } from '../i18n/formatters';

const STATUS_OPTIONS = ['', 'Queued', 'Running', 'Completed', 'Failed'];

function isFailed(job) {
  return String(job?.status || '').toLowerCase() === 'failed';
}

function JobDetail({ detail }) {
  const { t } = useTranslation();
  const job = detail?.job || detail?.Job || detail || {};
  const events = detail?.events || detail?.Events || [];
  return (
    <div>
      <dl className="kv-grid" style={{ marginBottom: '1rem' }}>
        <dt>ID</dt><dd><CopyableId value={getId(job)} /></dd>
        <dt>Status</dt><dd><StatusPill label={job.status} tone={toneForStatus(job.status)} /></dd>
        <dt>Link</dt><dd><CopyableId value={job.linkId} /></dd>
        <dt>Created</dt><dd>{formatDateTime(job.createdUtc)}</dd>
        <dt>Updated</dt><dd>{formatDateTime(job.updatedUtc || job.completedUtc)}</dd>
        {job.lastError && <><dt>Last Error</dt><dd style={{ color: 'var(--color-danger)' }}>{job.lastError}</dd></>}
      </dl>
      <h3 style={{ fontSize: 'var(--font-size-sm)', textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-secondary)', marginBottom: '0.75rem' }}>
        {t('jobs.timeline')}
      </h3>
      {events.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('jobs.noEvents')}</p>
      ) : (
        <ul className="timeline">
          {events.map((ev, i) => (
            <li className="timeline-item" key={i}>
              <span className={`timeline-marker ${toneForStatus(ev.status || ev.state)}`} />
              <div className="timeline-content">
                <div className="timeline-stage">{ev.stage || ev.name || ev.type || `Stage ${i + 1}`}</div>
                <div className="timeline-meta">
                  {(ev.status || ev.state) && <StatusPill label={ev.status || ev.state} tone={toneForStatus(ev.status || ev.state)} />}{' '}
                  {formatDateTime(ev.timestampUtc || ev.createdUtc || ev.time)}
                </div>
                {ev.message && <div className="timeline-meta">{ev.message}</div>}
                {ev.error && <div className="timeline-meta" style={{ color: 'var(--color-danger)' }}>{ev.error}</div>}
              </div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

function IngestionQueueView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [rows, setRows] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);
  const [status, setStatus] = useState('');
  const [modal, setModal] = useState(null);
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const resp = await apiClient.listJobs(status || undefined);
      setRows(normalizeList(resp).items);
    } catch (err) {
      setError(err?.message || 'Failed to load jobs');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [apiClient, status]);

  useEffect(() => { load(); }, [load]);

  const openDetail = useCallback(async (job) => {
    setModal({ type: 'detail', item: job });
    setDetail(null);
    setDetailLoading(true);
    try {
      const d = await apiClient.getJob(getId(job));
      setDetail(d);
    } catch {
      setDetail({ job });
    } finally {
      setDetailLoading(false);
    }
  }, [apiClient]);

  const restart = async (job) => {
    await apiClient.restartJob(getId(job));
    await load();
  };

  const remove = async (job) => {
    await apiClient.deleteJob(getId(job));
    setModal(null);
    await load();
  };

  const columns = [
    { key: 'id', label: 'Job ID', render: (r) => <CopyableId value={getId(r)} truncateLen={14} /> },
    { key: 'status', label: t('jobs.status'), render: (r) => <StatusPill label={r.status} tone={toneForStatus(r.status)} /> },
    { key: 'stage', label: 'Stage', render: (r) => r.stage || r.currentStage || '—' },
    { key: 'linkId', label: 'Link', render: (r) => <CopyableId value={r.linkId} truncateLen={12} /> },
    { key: 'createdUtc', label: 'Created', render: (r) => formatDateTime(r.createdUtc) },
    { key: 'updatedUtc', label: 'Updated', render: (r) => formatDateTime(r.updatedUtc || r.completedUtc) },
    { key: '_actions', label: t('common.actions'), sortable: false, width: '56px', render: (job) => (
      <ActionMenu items={[
        { key: 'view', label: t('common.view'), tip: 'Open this job’s details and its stage-by-stage progress.', onClick: () => openDetail(job) },
        { key: 'json', label: t('common.viewJson'), tip: 'Inspect the raw job record returned by the API.', onClick: () => setModal({ type: 'json', item: job }) },
        { key: 'restart', label: t('jobs.restart'), tip: 'Re-run this failed job from the beginning with the same settings.', hidden: !isFailed(job), onClick: () => setModal({ type: 'restart', item: job }) },
        { key: 'delete', label: t('jobs.delete'), tip: 'Delete this job and cascade-remove its graph nodes, indexed chunks, and logs.', danger: true, onClick: () => setModal({ type: 'delete', item: job }) }
      ]} />
    ) }
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
      </div>
      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}
      <DataTable columns={columns} data={rows} loading={loading} onRefresh={load} onRowClick={openDetail} />

      {modal?.type === 'detail' && (
        <Modal title={t('jobs.detail')} size="lg"
          headerExtra={(
            <>
              <CopyButton value={JSON.stringify(detail?.events ?? detail?.Events ?? [], null, 2)} label={t('jobs.copyLogs')} />
              {isFailed(modal.item) ? (
                <button type="button" className="button-primary" onClick={() => setModal({ type: 'restart', item: modal.item })}>{t('jobs.restart')}</button>
              ) : <CopyButton value={String(getId(modal.item))} label="ID" />}
            </>
          )}
          onClose={() => setModal(null)}
          footer={<button type="button" className="button-secondary" onClick={() => setModal(null)}>{t('common.close')}</button>}>
          {detailLoading ? <div className="table-loading"><div className="loading-spinner" /></div> : <JobDetail detail={detail} />}
        </Modal>
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
    </div>
  );
}

export default IngestionQueueView;
