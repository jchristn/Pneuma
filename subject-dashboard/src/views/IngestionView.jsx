import { useState, useEffect, useCallback, useMemo } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatRelativeTime, formatDateTime, formatDurationMs } from '../utils/format';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import BulkActionBar, { useTableSelection } from '../components/BulkActionBar';
import Modal from '../components/Modal';
import ConfirmModal from '../components/ConfirmModal';
import ActionMenu from '../components/ActionMenu';
import { stageLabel } from '../components/IngestionLogModal';
import StatusPill from '../components/StatusPill';
import CopyableId from '../components/CopyableId';
import JsonViewer from '../components/JsonViewer';
import './Ingestion.css';

const STAGES = [
  'ContentRetrieval',
  'TypeDetection',
  'CellExtraction',
  'Classification',
  'GraphMerge',
  'Embedding',
  'Indexing'
];

const STATUS_FILTERS = ['', 'Queued', 'Processing', 'Running', 'Completed', 'Failed'];

function isFailed(status) {
  const s = (status || '').toLowerCase();
  return s === 'failed' || s === 'error';
}

// Default table ordering: jobs that need attention or are in flight (anything NOT completed or queued)
// sort to the top; completed and queued jobs sink to the bottom. Lower rank sorts first.
function jobStateRank(status) {
  const s = (status || '').toLowerCase();
  if (s === 'completed') return 90;
  if (s === 'queued') return 80;
  if (s === 'failed' || s === 'error') return 0;
  if (s === 'processing') return 10;
  if (s === 'running') return 11;
  if (s === 'cancelled' || s === 'canceled') return 20;
  return 15; // any other in-flight/unknown state still sorts above queued/completed
}

function readEventStage(ev) {
  return ev.stage || ev.stageName || ev.name || ev.type || '';
}

function StageTimeline({ events }) {
  const { t } = useTranslation();
  const byStage = {};
  for (const ev of events || []) {
    const stage = readEventStage(ev);
    if (stage) byStage[stage.toLowerCase()] = ev;
  }

  return (
    <div className="stage-timeline">
      {STAGES.map((stage, idx) => {
        const ev = byStage[stage.toLowerCase()];
        const status = ev?.status || 'Pending';
        const failed = isFailed(status);
        const done = ['completed', 'ingested', 'succeeded', 'success'].includes(
          (status || '').toLowerCase()
        );
        return (
          <div className={`stage-row ${failed ? 'stage-failed' : done ? 'stage-done' : ''}`} key={stage}>
            <div className="stage-marker">
              <span className="stage-dot" />
              {idx < STAGES.length - 1 && <span className="stage-line" />}
            </div>
            <div className="stage-content">
              <div className="stage-head">
                <span className="stage-name">{stageLabel(stage)}</span>
                <StatusPill status={status} />
              </div>
              {ev?.message && <div className="stage-message">{ev.message}</div>}
              <div className="stage-meta">
                {ev?.durationMs != null && <span>{t('ingestion.duration')}: {formatDurationMs(ev.durationMs)}</span>}
                {Number(ev?.queueDurationMs) > 0 && <span>{t('ingestion.queueDuration')}: {formatDurationMs(ev.queueDurationMs)}</span>}
                {(ev?.timestampUtc || ev?.updatedUtc || ev?.createdUtc) && (
                  <span title={formatDateTime(ev.timestampUtc || ev.updatedUtc || ev.createdUtc)}>
                    {formatRelativeTime(ev.timestampUtc || ev.updatedUtc || ev.createdUtc)}
                  </span>
                )}
              </div>
            </div>
          </div>
        );
      })}
    </div>
  );
}

const PERF_COLORS = ['#4dabf7', '#38d9a9', '#a9e34b', '#ffd43b', '#ffa94d', '#ff6b6b', '#da77f2', '#845ef7', '#20c997'];

// Horizontal bar per timed stage, sized by its duration, so a viewer can see where ingestion spent time.
function StagePerfBars({ events }) {
  const { t } = useTranslation();
  const stages = (events || [])
    .map((ev) => ({ stage: readEventStage(ev), ms: Number(ev?.durationMs) || 0 }))
    .filter((s) => s.stage && s.ms > 0);
  if (stages.length === 0) return null;
  const totalMs = stages.reduce((sum, s) => sum + s.ms, 0);
  const maxMs = stages.reduce((max, s) => Math.max(max, s.ms), 0);
  return (
    <div className="hd-section" style={{ marginTop: 20 }}>
      <div className="hd-section-title">{t('ingestion.timePerStage', 'Time per stage')}</div>
      <div className="hd-timing">
        {stages.map((s, i) => {
          const pct = maxMs > 0 ? Math.max(2, (s.ms / maxMs) * 100) : 0;
          const share = totalMs > 0 ? ((s.ms / totalMs) * 100).toFixed(0) : '0';
          return (
            <div className="hd-timing-row" key={`${s.stage}-${i}`} title={`${stageLabel(s.stage)}: ${formatDurationMs(s.ms)} (${share}%)`}>
              <span className="hd-timing-label">{stageLabel(s.stage)}</span>
              <span className="hd-timing-track">
                <span className="hd-timing-fill" style={{ width: `${Math.min(pct, 100)}%`, background: PERF_COLORS[i % PERF_COLORS.length] }} />
              </span>
              <span className="hd-timing-value">{formatDurationMs(s.ms)} · {share}%</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

function IngestionView() {
  const { apiClient } = useAuth();
  const { t } = useTranslation();

  const [jobs, setJobs] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [statusFilter, setStatusFilter] = useState('');

  const [detailOpen, setDetailOpen] = useState(false);
  const [detail, setDetail] = useState(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [restarting, setRestarting] = useState(false);

  const load = useCallback(async () => {
    if (!apiClient) return;
    setLoading(true);
    setError('');
    try {
      const res = await apiClient.getJobs(statusFilter ? { status: statusFilter, maxResults: 1000 } : { maxResults: 1000 });
      setJobs(asArray(res, 'jobs'));
    } catch (err) {
      setError(err.message);
    } finally {
      setLoading(false);
    }
  }, [apiClient, statusFilter]);

  useEffect(() => {
    load();
  }, [load]);

  // Sort by state (attention/in-flight first), then most-recently-updated within a state group.
  const sortedJobs = useMemo(() => {
    return [...jobs].sort((a, b) => {
      const r = jobStateRank(a.status) - jobStateRank(b.status);
      if (r !== 0) return r;
      const at = a.updatedUtc || a.lastUpdatedUtc || a.createdUtc || '';
      const bt = b.updatedUtc || b.lastUpdatedUtc || b.createdUtc || '';
      return bt.localeCompare(at);
    });
  }, [jobs]);

  const openDetail = async (job) => {
    setDetailOpen(true);
    setDetail(null);
    setDetailLoading(true);
    try {
      const res = await apiClient.getJob(job.id);
      // Response may be { job, events } or a flat job with events.
      const normalized = res && res.job ? res : { job: res, events: res?.events || [] };
      setDetail({ ...normalized, _row: job });
    } catch (err) {
      setError(err.message);
      setDetailOpen(false);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleRestart = async (job) => {
    setRestarting(true);
    try {
      await apiClient.restartJob(job.id);
      setDetailOpen(false);
      await load();
    } catch (err) {
      setError(err.message);
    } finally {
      setRestarting(false);
    }
  };

  const { selectedItems, clear, selection } = useTableSelection(jobs);
  const failedSelected = selectedItems.filter((j) => isFailed(j.status));
  const [bulkRestartOpen, setBulkRestartOpen] = useState(false);
  const [bulkRestarting, setBulkRestarting] = useState(false);

  const handleBulkRestart = async () => {
    setBulkRestarting(true);
    try {
      for (const job of failedSelected) {
        await apiClient.restartJob(job.id);
      }
      setBulkRestartOpen(false);
      clear();
      await load();
    } catch (err) {
      setError(err.message);
    } finally {
      setBulkRestarting(false);
    }
  };

  const bulkBar = (
    <BulkActionBar
      count={selectedItems.length}
      onClear={clear}
      actions={[
        { key: 'restart', label: t('common.restart'), disabled: failedSelected.length === 0,
          tip: failedSelected.length === 0 ? t('ingestion.bulkRestartNone', 'Only failed jobs can be restarted.') : t('ingestion.bulkRestartTip', { count: failedSelected.length, defaultValue: `Restart ${failedSelected.length} failed job(s).` }),
          onClick: () => setBulkRestartOpen(true) }
      ]}
    />
  );

  const columns = [
    {
      key: 'id',
      label: t('ingestion.job'),
      className: 'cell-id',
      render: (v) => <CopyableId value={v} title="Copy job ID" />
    },
    {
      key: 'linkUrl',
      label: t('ingestion.link'),
      sortable: false,
      className: 'cell-url',
      render: (v, row) => {
        const url = v || row.url || row.linkTitle || row.linkId || '—';
        return <span title={url}>{url}</span>;
      }
    },
    {
      key: 'status',
      label: t('common.status'),
      render: (v) => <StatusPill status={v} />
    },
    {
      key: 'createdUtc',
      label: t('ingestion.created'),
      sortAccessor: (row) => row.createdUtc || '',
      render: (v) => (v ? <span title={formatDateTime(v)}>{formatRelativeTime(v)}</span> : '—')
    },
    {
      key: 'updatedUtc',
      label: t('ingestion.updated'),
      sortAccessor: (row) => row.updatedUtc || row.lastUpdatedUtc || '',
      render: (v, row) => {
        const val = v || row.lastUpdatedUtc;
        return val ? <span title={formatDateTime(val)}>{formatRelativeTime(val)}</span> : '—';
      }
    },
    {
      key: '_actions',
      label: t('common.actions'),
      className: 'actions-column',
      sortable: false,
      render: (_v, row) => (
        <ActionMenu
          actions={[
            { label: t('common.view'), onClick: () => openDetail(row) },
            ...(isFailed(row.status)
              ? [{ label: t('common.restart'), onClick: () => handleRestart(row) }]
              : [])
          ]}
        />
      )
    }
  ];

  const toolbar = (
    <div className="pagination-group">
      <label>{t('ingestion.filterStatus')}:</label>
      <select value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
        {STATUS_FILTERS.map((s) => (
          <option key={s || 'all'} value={s}>
            {s || t('common.all')}
          </option>
        ))}
      </select>
    </div>
  );

  const detailJob = detail?.job || {};
  const detailFailed = isFailed(detailJob.status || detail?._row?.status);

  return (
    <div>
      <PageHeader title={t('ingestion.title')} subtitle={t('ingestion.subtitle')} />

      {error && <div className="error-banner">{error}</div>}

      <DataTable
        columns={columns}
        data={sortedJobs}
        loading={loading}
        onRefresh={load}
        toolbar={toolbar}
        onRowClick={openDetail}
        selection={selection}
        bulkBar={bulkBar}
        emptyTitle={t('ingestion.title')}
        emptyDescription={t('ingestion.empty')}
      />

      <Modal
        isOpen={detailOpen}
        onClose={() => setDetailOpen(false)}
        title={t('ingestion.timeline')}
        size="large"
        headerAction={
          detailFailed && detailJob.id ? (
            <button className="btn btn-primary btn-sm" onClick={() => handleRestart(detailJob)} disabled={restarting}>
              {restarting ? t('common.loading') : t('ingestion.restart')}
            </button>
          ) : null
        }
      >
        {detailLoading ? (
          <div className="loading-block">
            <span className="loading-spinner" /> {t('common.loading')}
          </div>
        ) : detail ? (
          <div>
            <div className="detail-grid" style={{ marginBottom: 20 }}>
              <div className="detail-item">
                <span className="detail-label">{t('ingestion.job')}</span>
                <span className="detail-value"><CopyableId value={detailJob.id || detail._row?.id} /></span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('common.status')}</span>
                <span className="detail-value">
                  <StatusPill status={detailJob.status || detail._row?.status} />
                </span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('ingestion.created')}</span>
                <span className="detail-value">
                  {formatDateTime(detailJob.createdUtc || detail._row?.createdUtc)}
                </span>
              </div>
              <div className="detail-item">
                <span className="detail-label">{t('ingestion.updated')}</span>
                <span className="detail-value">
                  {formatDateTime(detailJob.updatedUtc || detailJob.lastUpdatedUtc || detail._row?.updatedUtc)}
                </span>
              </div>
            </div>

            <h4 style={{ marginBottom: 14 }}>{t('ingestion.stages')}</h4>
            <StageTimeline events={detail.events} />
            <StagePerfBars events={detail.events} />

            <div style={{ marginTop: 20 }}>
              <JsonViewer value={detail} label="Raw JSON" />
            </div>
          </div>
        ) : null}
      </Modal>

      <ConfirmModal
        isOpen={bulkRestartOpen}
        onClose={() => setBulkRestartOpen(false)}
        onConfirm={handleBulkRestart}
        title={t('common.restart')}
        message={`Restart ${failedSelected.length} failed job(s) from the beginning?`}
        confirmLabel={t('common.restart')}
        isLoading={bulkRestarting}
      />
    </div>
  );
}

export default IngestionView;
