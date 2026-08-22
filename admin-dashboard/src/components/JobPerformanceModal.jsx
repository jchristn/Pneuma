import { useState, useEffect, useCallback } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import Modal from './Modal';
import CopyButton from './CopyButton';
import { stageLabel, normalizeKey } from './IngestionTimeline';
import { formatDuration } from '../i18n/formatters';

// Per-stage bar colors, keyed by normalized stage name (falls back to a rotating palette).
const STAGE_COLORS = {
  contentretrieval: '#4dabf7',
  typedetection: '#3bc9db',
  cellextraction: '#38d9a9',
  classification: '#a9e34b',
  graphmerge: '#ffd43b',
  summarization: '#ffa94d',
  chunking: '#ff922b',
  embedding: '#da77f2',
  indexing: '#ff6b6b'
};
const PALETTE = ['#4dabf7', '#38d9a9', '#a9e34b', '#ffd43b', '#ffa94d', '#ff6b6b', '#da77f2', '#845ef7', '#20c997'];

function colorFor(stage, idx) {
  return STAGE_COLORS[normalizeKey(stage)] || PALETTE[idx % PALETTE.length];
}

/**
 * Visualizes where an ingestion job spent its time: one horizontal bar per stage, sized by the stage's
 * measured duration, with the discrete duration (and share of the total) shown alongside.
 */
export default function JobPerformanceModal({ job, onClose }) {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [detail, setDetail] = useState(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const jobId = job?.id || job?.Id;

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await apiClient.getJobLog(jobId);
      setDetail(data);
      setError('');
    } catch (err) {
      setError(err?.message || 'Failed to load job performance');
    } finally {
      setLoading(false);
    }
  }, [apiClient, jobId]);

  useEffect(() => { load(); }, [load]);

  const jobData = detail?.job || detail?.Job || job || {};
  const rawEvents = Array.isArray(detail?.events || detail?.Events) ? (detail.events || detail.Events) : [];
  // Only timed stages have a bar; phase markers report 0ms.
  const stages = rawEvents
    .map((ev) => ({ stage: ev.stage, ms: Number(ev.durationMs) || 0, queueMs: Number(ev.queueDurationMs) || 0 }))
    .filter((s) => s.ms > 0);
  const totalMs = stages.reduce((sum, s) => sum + s.ms, 0);
  const maxMs = stages.reduce((max, s) => Math.max(max, s.ms), 0);
  const queueMs = stages.reduce((sum, s) => sum + s.queueMs, 0);

  return (
    <Modal
      title={t('jobs.viewPerformance', 'Ingestion performance')}
      size="wide"
      subtitle={jobData.sourceUrl || jobData.linkId}
      headerExtra={<CopyButton value={String(jobId)} label="ID" />}
      onClose={onClose}
      footer={<button type="button" className="button-secondary" onClick={onClose}>{t('common.close', 'Close')}</button>}
    >
      {loading && !detail && <div className="table-loading"><div className="loading-spinner" /></div>}
      {error && <div className="error-message" style={{ marginBottom: '0.75rem' }}>{error}</div>}

      {detail && stages.length === 0 && !loading && (
        <div className="ilog-empty">{t('jobs.noPerformance', 'No timed stages recorded for this job yet.')}</div>
      )}

      {stages.length > 0 && (
        <>
          <div className="hd-metrics" style={{ marginBottom: '1rem' }}>
            <div className="hd-metric"><span className="hd-metric-label">{t('jobs.totalRuntime', 'Total runtime')}</span><span className="hd-metric-value">{formatDuration(totalMs)}</span></div>
            <div className="hd-metric"><span className="hd-metric-label">{t('jobs.stageCount', 'Stages')}</span><span className="hd-metric-value">{stages.length}</span></div>
            {queueMs > 0 && <div className="hd-metric"><span className="hd-metric-label">{t('jobs.queueTime', 'Queue wait')}</span><span className="hd-metric-value">{formatDuration(queueMs)}</span></div>}
          </div>
          <div className="hd-section">
            <div className="hd-section-title">{t('jobs.stageLatency', 'Time per stage')}</div>
            <div className="hd-timing">
              {stages.map((s, i) => {
                const pct = maxMs > 0 ? Math.max(2, (s.ms / maxMs) * 100) : 0;
                const share = totalMs > 0 ? ((s.ms / totalMs) * 100).toFixed(0) : '0';
                return (
                  <div className="hd-timing-row" key={`${s.stage}-${i}`} title={`${stageLabel(s.stage)}: ${formatDuration(s.ms)} (${share}% of total)`}>
                    <span className="hd-timing-label">{stageLabel(s.stage)}</span>
                    <span className="hd-timing-track">
                      <span className="hd-timing-fill" style={{ width: `${Math.min(pct, 100)}%`, background: colorFor(s.stage, i) }} />
                    </span>
                    <span className="hd-timing-value">{formatDuration(s.ms)} · {share}%</span>
                  </div>
                );
              })}
            </div>
          </div>
        </>
      )}
    </Modal>
  );
}
