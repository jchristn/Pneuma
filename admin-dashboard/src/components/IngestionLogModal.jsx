import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import Modal from './Modal';
import StatusPill, { toneForStatus } from './StatusPill';
import CopyButton from './CopyButton';
import { formatDateTime, formatDuration } from '../i18n/formatters';
import './IngestionLog.css';

// Friendly labels for the ordered ingestion stages emitted by the backend.
const STAGE_LABELS = {
  pending: 'Started',
  contentretrieval: 'Content retrieval',
  typedetection: 'Type detection',
  cellextraction: 'Semantic cell extraction',
  classification: 'Ontology / knowledge-graph mapping',
  graphmerge: 'Knowledge-graph insertion',
  summarization: 'Summarization',
  chunking: 'Chunking',
  embedding: 'Embedding',
  indexing: 'Search indexing',
  done: 'Complete'
};

function normalizeKey(value) {
  return String(value ?? '').toLowerCase().replace(/[\s_-]/g, '');
}

function stageLabel(stage) {
  if (!stage) return '—';
  return STAGE_LABELS[normalizeKey(stage)] || String(stage);
}

function stateFor(status) {
  const key = normalizeKey(status);
  if (['completed', 'complete', 'success', 'succeeded', 'done', 'ingested'].includes(key)) return 'is-done';
  if (['failed', 'error', 'failure'].includes(key)) return 'is-failed';
  if (['processing', 'running', 'inprogress'].includes(key)) return 'is-processing';
  return '';
}

function runTimestamp(run) {
  const job = run?.job || {};
  return new Date(job.createdUtc || job.CreatedUtc || 0).getTime() || 0;
}

function StepTimeline({ events }) {
  const { t } = useTranslation();
  return (
    <div className="ilog-timeline">
      {events.map((ev, idx) => (
        <div className={`ilog-row ${stateFor(ev.status)}`} key={idx}>
          <div className="ilog-marker">
            <span className="ilog-dot" />
            {idx < events.length - 1 && <span className="ilog-line" />}
          </div>
          <div className="ilog-content">
            <div className="ilog-head">
              <span className="ilog-name">{stageLabel(ev.stage)}</span>
              <StatusPill label={ev.status} tone={toneForStatus(ev.status)} />
            </div>
            {ev.message && <div className="ilog-message">{ev.message}</div>}
            <div className="ilog-meta">
              {Number(ev.durationMs) > 0 && (
                <span>{t('ingestionLog.duration')}: {formatDuration(ev.durationMs)}</span>
              )}
              {Number(ev.queueDurationMs) > 0 && (
                <span>{t('ingestionLog.queueDuration')}: {formatDuration(ev.queueDurationMs)}</span>
              )}
              {ev.createdUtc && <span>{formatDateTime(ev.createdUtc)}</span>}
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

function IngestionRun({ run, index, total }) {
  const { t } = useTranslation();
  const job = run.job || {};
  const events = Array.isArray(run.events) ? run.events : [];
  const runLabel = total > 1 ? `${t('ingestionLog.run')} ${total - index}` : t('ingestionLog.run');

  return (
    <div className="ilog-run">
      <div className="ilog-run-header">
        <span className="ilog-run-title">{runLabel}</span>
        {job.documentType && <span className="ilog-doctype">{job.documentType}</span>}
        <StatusPill label={job.status} tone={toneForStatus(job.status)} />
        <span className="ilog-run-times">
          {job.createdUtc && <span>{t('ingestionLog.created')}: {formatDateTime(job.createdUtc)}</span>}
          {job.completedUtc && <span>{t('ingestionLog.completed')}: {formatDateTime(job.completedUtc)}</span>}
        </span>
      </div>
      {job.error && <div className="ilog-error">{job.error}</div>}
      {events.length > 0
        ? <StepTimeline events={events} />
        : <div className="ilog-empty">{t('ingestionLog.emptyQueued')}</div>}
    </div>
  );
}

function IngestionLogModal({ link, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [runs, setRuns] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(() => {
    let cancelled = false;
    setLoading(true);
    setError('');
    apiClient.getLinkIngestionLog(link.id)
      .then((data) => {
        if (cancelled) return;
        const items = normalizeList(data).items;
        const sorted = [...items].sort((a, b) => runTimestamp(b) - runTimestamp(a));
        setRuns(sorted);
      })
      .catch((err) => { if (!cancelled) setError(err?.message || 'Failed to load ingestion log'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient, link.id]);

  useEffect(() => load(), [load]);

  return (
    <Modal
      title={t('ingestionLog.title')}
      size="lg"
      subtitle={link.title || link.url}
      headerExtra={<CopyButton value={String(link.id)} label="ID" />}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={() => load()} disabled={loading}>
            {t('common.refresh')}
          </button>
          <button type="button" className="button-secondary" onClick={onClose}>{t('common.close')}</button>
        </>
      )}
    >
      {loading && <div className="table-loading"><div className="loading-spinner" /></div>}
      {!loading && error && (
        <div>
          <div className="error-message">{error}</div>
          <div style={{ marginTop: '0.75rem' }}>
            <button type="button" className="button-secondary" onClick={() => load()}>{t('common.retry')}</button>
          </div>
        </div>
      )}
      {!loading && !error && runs.length === 0 && (
        <div className="ilog-empty">{t('ingestionLog.emptyNoRuns')}</div>
      )}
      {!loading && !error && runs.map((run, idx) => (
        <IngestionRun key={idx} run={run} index={idx} total={runs.length} />
      ))}
    </Modal>
  );
}

export default IngestionLogModal;
