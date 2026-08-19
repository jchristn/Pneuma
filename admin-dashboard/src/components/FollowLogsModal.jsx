import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import Modal from './Modal';
import StatusPill, { toneForStatus } from './StatusPill';
import CopyButton from './CopyButton';
import { formatDateTime, formatDuration } from '../i18n/formatters';
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
  cancelled: 'Cancelled',
  done: 'Complete'
};

// Auto-refresh cadence options (seconds) for the follow-logs view; 0 means "None" (manual only).
const REFRESH_OPTIONS = [0, 5, 10, 30, 60, 120, 180, 300];
const DEFAULT_REFRESH_SECONDS = 5;

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
  if (['cancelled', 'canceled'].includes(key)) return 'is-failed';
  if (['processing', 'running', 'inprogress'].includes(key)) return 'is-processing';
  return '';
}

function isTerminal(status) {
  const key = normalizeKey(status);
  return ['completed', 'complete', 'failed', 'error', 'cancelled', 'canceled', 'done', 'ingested'].includes(key);
}

function FollowLogsModal({ job, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [lastUpdated, setLastUpdated] = useState(null);
  const [refreshSeconds, setRefreshSeconds] = useState(DEFAULT_REFRESH_SECONDS);

  const jobId = job?.id || job?.Id;

  const poll = useCallback(async () => {
    try {
      const data = await apiClient.getJobLog(jobId);
      setDetail(data);
      setError('');
      setLastUpdated(new Date());
    } catch (err) {
      setError(err?.message || 'Failed to load job log');
    } finally {
      setLoading(false);
    }
  }, [apiClient, jobId]);

  const jobData = detail?.job || detail?.Job || job || {};
  const events = Array.isArray(detail?.events || detail?.Events) ? (detail.events || detail.Events) : [];
  const running = !isTerminal(jobData.status);

  // Derive the currently-running step (the stage the job reports that hasn't yet emitted a completion
  // event) so it can be shown as a grey in-progress bubble with a live runtime.
  const lastEvent = events.length > 0 ? events[events.length - 1] : null;
  const currentStageRaw = jobData.stage || jobData.currentStage;
  const currentKey = normalizeKey(currentStageRaw);
  const lastKey = lastEvent ? normalizeKey(lastEvent.stage) : '';
  const showCurrentStep = running && !!currentStageRaw && currentKey !== 'done' && currentKey !== lastKey;
  const currentStart = lastEvent?.createdUtc || jobData.startedUtc || jobData.createdUtc || null;
  const currentRuntimeMs = currentStart ? Math.max(0, Date.now() - new Date(currentStart).getTime()) : null;

  // Total overall runtime = the sum of every stage's measured duration (phase markers report 0), plus the
  // live elapsed time of the step currently in progress, so the header total reflects work done so far.
  const totalRuntimeMs = events.reduce((sum, ev) => sum + (Number(ev.durationMs) || 0), 0)
    + (showCurrentStep && currentRuntimeMs ? currentRuntimeMs : 0);

  // Initial load (and reload when the target job changes).
  useEffect(() => {
    poll();
  }, [poll]);

  // Auto-refresh on the selected cadence, but never once the job reaches a terminal state.
  useEffect(() => {
    if (refreshSeconds <= 0 || !running) return undefined;
    const id = setInterval(poll, refreshSeconds * 1000);
    return () => clearInterval(id);
  }, [refreshSeconds, running, poll]);

  return (
    <Modal
      title={t('jobs.followLogs')}
      size="wide"
      subtitle={jobData.sourceUrl || jobData.linkId}
      headerExtra={(
        <>
          <CopyButton value={JSON.stringify(events, null, 2)} label={t('jobs.copyLogs')} />
          <CopyButton value={String(jobId)} label="ID" />
        </>
      )}
      onClose={onClose}
      footer={(
        <>
          <button type="button" className="button-secondary" onClick={() => poll()} disabled={loading}>
            {t('common.refresh')}
          </button>
          <button type="button" className="button-secondary" onClick={onClose}>{t('common.close')}</button>
        </>
      )}
    >
      <div className="ilog-run-header" style={{ marginBottom: '0.75rem' }}>
        <StatusPill label={jobData.status} tone={toneForStatus(jobData.status)} />
        {jobData.documentType && <span className="ilog-doctype">{jobData.documentType}</span>}
        {(events.length > 0 || showCurrentStep) && (
          <span className="ilog-total">{t('jobs.totalRuntime')}: <strong>{formatDuration(totalRuntimeMs)}</strong></span>
        )}
        <span className="ilog-run-times">
          {running && refreshSeconds > 0 && (
            <span className="ilog-live" aria-live="polite">
              {t('jobs.autoRefreshing', { seconds: refreshSeconds })}
            </span>
          )}
          {lastUpdated && <span>{t('jobs.lastUpdated')}: {formatDateTime(lastUpdated.toISOString())}</span>}
          <label className="ilog-refresh">
            <span>{t('jobs.autoRefresh')}</span>
            <select
              value={refreshSeconds}
              onChange={(e) => setRefreshSeconds(Number(e.target.value))}
              aria-label={t('jobs.autoRefresh')}
            >
              {REFRESH_OPTIONS.map((s) => (
                <option key={s} value={s}>
                  {s === 0 ? t('common.none') : t('jobs.refreshEvery', { seconds: s })}
                </option>
              ))}
            </select>
          </label>
        </span>
      </div>

      {jobData.error && <div className="ilog-error">{jobData.error}</div>}

      {loading && !detail && <div className="table-loading"><div className="loading-spinner" /></div>}
      {error && <div className="error-message" style={{ marginBottom: '0.75rem' }}>{error}</div>}

      {detail && events.length === 0 && (
        <div className="ilog-empty">{t('ingestionLog.emptyQueued')}</div>
      )}

      {(events.length > 0 || showCurrentStep) && (
        <div className="ilog-timeline">
          {events.map((ev, idx) => (
            <div className={`ilog-row ${stateFor(ev.status)}`} key={idx}>
              <div className="ilog-marker">
                <span className="ilog-dot" />
                {(idx < events.length - 1 || showCurrentStep) && <span className="ilog-line" />}
              </div>
              <div className="ilog-content">
                <div className="ilog-head">
                  <span className="ilog-name">{stageLabel(ev.stage)}</span>
                  <StatusPill label={ev.status} tone={toneForStatus(ev.status)} />
                </div>
                {ev.message && <div className="ilog-message">{ev.message}</div>}
                <div className="ilog-meta">
                  {ev.durationMs != null && ev.durationMs !== '' && (
                    <span>{t('ingestionLog.duration')}: {formatDuration(ev.durationMs)}</span>
                  )}
                  {ev.createdUtc && <span>{formatDateTime(ev.createdUtc)}</span>}
                </div>
              </div>
            </div>
          ))}
          {showCurrentStep && (
            <div className="ilog-row is-current">
              <div className="ilog-marker"><span className="ilog-dot" /></div>
              <div className="ilog-content">
                <div className="ilog-head">
                  <span className="ilog-name">{stageLabel(currentStageRaw)}</span>
                  <StatusPill label={t('jobs.inProgress')} tone="neutral" />
                </div>
                <div className="ilog-meta">
                  {currentRuntimeMs != null && <span>{t('jobs.runningFor')}: {formatDuration(currentRuntimeMs)}</span>}
                  {currentStart && <span>{formatDateTime(currentStart)}</span>}
                </div>
              </div>
            </div>
          )}
        </div>
      )}
    </Modal>
  );
}

export default FollowLogsModal;
