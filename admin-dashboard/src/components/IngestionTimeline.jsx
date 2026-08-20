import StatusPill, { toneForStatus } from './StatusPill';
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
  cancelled: 'Cancelled',
  done: 'Complete'
};

export function normalizeKey(value) {
  return String(value ?? '').toLowerCase().replace(/[\s_-]/g, '');
}

export function stageLabel(stage) {
  if (!stage) return '—';
  return STAGE_LABELS[normalizeKey(stage)] || String(stage);
}

export function stateFor(status) {
  const key = normalizeKey(status);
  if (['completed', 'complete', 'success', 'succeeded', 'done', 'ingested'].includes(key)) return 'is-done';
  if (['failed', 'error', 'failure'].includes(key)) return 'is-failed';
  if (['cancelled', 'canceled'].includes(key)) return 'is-failed';
  if (['processing', 'running', 'inprogress'].includes(key)) return 'is-processing';
  return '';
}

export function isTerminal(status) {
  const key = normalizeKey(status);
  return ['completed', 'complete', 'failed', 'error', 'cancelled', 'canceled', 'done', 'ingested'].includes(key);
}

/**
 * Renders an ingestion job's stage timeline (completed events plus the in-progress step). Shared by the
 * Follow-Logs modal and the first-run setup wizard so both surfaces read identically.
 */
export default function IngestionTimeline({ jobData, events, inProgressLabel, runningForLabel }) {
  const list = Array.isArray(events) ? events : [];
  const running = !isTerminal(jobData?.status);

  // Derive the currently-running step: the stage the job reports that has not yet emitted a completion event.
  const lastEvent = list.length > 0 ? list[list.length - 1] : null;
  const currentStageRaw = jobData?.stage || jobData?.currentStage;
  const currentKey = normalizeKey(currentStageRaw);
  const lastKey = lastEvent ? normalizeKey(lastEvent.stage) : '';
  const showCurrentStep = running && !!currentStageRaw && currentKey !== 'done' && currentKey !== lastKey;
  const currentStart = lastEvent?.createdUtc || jobData?.startedUtc || jobData?.createdUtc || null;
  const currentRuntimeMs = currentStart ? Math.max(0, Date.now() - new Date(currentStart).getTime()) : null;

  if (list.length === 0 && !showCurrentStep) return null;

  return (
    <div className="ilog-timeline">
      {list.map((ev, idx) => (
        <div className={`ilog-row ${stateFor(ev.status)}`} key={idx}>
          <div className="ilog-marker">
            <span className="ilog-dot" />
            {(idx < list.length - 1 || showCurrentStep) && <span className="ilog-line" />}
          </div>
          <div className="ilog-content">
            <div className="ilog-head">
              <span className="ilog-name">{stageLabel(ev.stage)}</span>
              <StatusPill label={ev.status} tone={toneForStatus(ev.status)} />
            </div>
            {ev.message && <div className="ilog-message">{ev.message}</div>}
            <div className="ilog-meta">
              {Number(ev.durationMs) > 0 && <span>{formatDuration(ev.durationMs)}</span>}
              {Number(ev.queueDurationMs) > 0 && <span>Queue duration: {formatDuration(ev.queueDurationMs)}</span>}
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
              <StatusPill label={inProgressLabel || 'In progress'} tone="neutral" />
            </div>
            <div className="ilog-meta">
              {currentRuntimeMs != null && <span>{runningForLabel || 'Running for'}: {formatDuration(currentRuntimeMs)}</span>}
              {currentStart && <span>{formatDateTime(currentStart)}</span>}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
