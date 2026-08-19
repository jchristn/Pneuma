import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { asArray } from '../utils/api';
import { formatDateTime, formatDurationMs } from '../utils/format';
import Modal from './Modal';
import StatusPill from './StatusPill';
import CopyableId from './CopyableId';
import './IngestionLog.css';

// Friendly labels for the ordered ingestion stages emitted by the backend.
const STAGE_LABELS = {
  pending: 'Started',
  typedetection: 'Type detection',
  cellextraction: 'Semantic cell extraction',
  classification: 'Ontology / knowledge-graph mapping',
  graphmerge: 'Knowledge-graph insertion',
  embedding: 'Chunking & embedding',
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
  return new Date(job.createdUtc || 0).getTime() || 0;
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
              <StatusPill status={ev.status} />
            </div>
            {ev.message && <div className="ilog-message">{ev.message}</div>}
            <div className="ilog-meta">
              {ev.durationMs != null && ev.durationMs !== '' && (
                <span>{t('ingestionLog.duration')}: {formatDurationMs(ev.durationMs)}</span>
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
        <StatusPill status={job.status} />
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

function IngestionLogModal({ isOpen, link, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [runs, setRuns] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(() => {
    if (!apiClient || !link) return undefined;
    let cancelled = false;
    setLoading(true);
    setError('');
    apiClient.getLinkIngestionLog(link.id)
      .then((data) => {
        if (cancelled) return;
        const items = asArray(data, 'runs', 'logs');
        const sorted = [...items].sort((a, b) => runTimestamp(b) - runTimestamp(a));
        setRuns(sorted);
      })
      .catch((err) => { if (!cancelled) setError(err?.message || 'Failed to load ingestion log'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [apiClient, link]);

  useEffect(() => {
    if (!isOpen) return undefined;
    return load();
  }, [isOpen, load]);

  return (
    <Modal
      isOpen={isOpen}
      onClose={onClose}
      title={t('ingestionLog.title')}
      size="large"
      headerAction={link ? <CopyableId value={link.id} title="Copy link ID" /> : null}
    >
      {loading ? (
        <div className="loading-block">
          <span className="loading-spinner" /> {t('common.loading')}
        </div>
      ) : error ? (
        <div>
          <div className="form-error">{error}</div>
          <div className="ilog-actions">
            <button type="button" className="btn btn-secondary" onClick={() => load()}>{t('common.retry')}</button>
            <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
          </div>
        </div>
      ) : (
        <div>
          {runs.length === 0 ? (
            <div className="ilog-empty">{t('ingestionLog.emptyNoRuns')}</div>
          ) : (
            runs.map((run, idx) => (
              <IngestionRun key={idx} run={run} index={idx} total={runs.length} />
            ))
          )}
          <div className="ilog-actions">
            <button type="button" className="btn btn-secondary" onClick={() => load()}>{t('common.refresh')}</button>
            <button type="button" className="btn btn-primary" onClick={onClose}>{t('common.close')}</button>
          </div>
        </div>
      )}
    </Modal>
  );
}

export default IngestionLogModal;
