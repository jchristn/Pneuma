import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatRelativeTime, formatDateTime, formatDurationMs } from '../utils/format';
import PageHeader from '../components/PageHeader';
import DataTable from '../components/DataTable';
import Modal from '../components/Modal';
import ActionMenu from '../components/ActionMenu';
import StatusPill from '../components/StatusPill';
import CopyableId from '../components/CopyableId';
import JsonViewer from '../components/JsonViewer';
import './Ingestion.css';

const STAGES = [
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
                <span className="stage-name">{stage}</span>
                <StatusPill status={status} />
              </div>
              {ev?.message && <div className="stage-message">{ev.message}</div>}
              <div className="stage-meta">
                {ev?.durationMs != null && <span>{t('ingestion.duration')}: {formatDurationMs(ev.durationMs)}</span>}
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
        data={jobs}
        loading={loading}
        onRefresh={load}
        toolbar={toolbar}
        onRowClick={openDetail}
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

            <div style={{ marginTop: 20 }}>
              <JsonViewer value={detail} label="Raw JSON" />
            </div>
          </div>
        ) : null}
      </Modal>
    </div>
  );
}

export default IngestionView;
