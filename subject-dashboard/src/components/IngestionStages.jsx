import { useTranslation } from 'react-i18next';
import { formatRelativeTime, formatDateTime, formatDurationMs } from '../utils/format';
import { stageLabel } from './IngestionLogModal';
import StatusPill from './StatusPill';

// The ingestion job detail's stage visuals: a timeline of the pipeline stages with each one's status and timing,
// and a bar per timed stage showing where the job spent its time.

const STAGES = [
  'ContentRetrieval',
  'TypeDetection',
  'CellExtraction',
  'Classification',
  'GraphMerge',
  'Embedding',
  'Indexing'
];

function isFailed(status) {
  const s = (status || '').toLowerCase();
  return s === 'failed' || s === 'error';
}

function readEventStage(ev) {
  return ev.stage || ev.stageName || ev.name || ev.type || '';
}

export function StageTimeline({ events }) {
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
export function StagePerfBars({ events }) {
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
