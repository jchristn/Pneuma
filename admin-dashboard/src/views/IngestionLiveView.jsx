import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import { stageLabel } from '../components/IngestionTimeline';

const POLL_MS = 2000;
const TICK_MS = 1000;

// Human-friendly elapsed duration from a millisecond span.
function formatElapsed(ms) {
  const totalSeconds = Math.max(0, Math.floor(ms / 1000));
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  if (minutes < 60) return `${minutes}m ${seconds}s`;
  const hours = Math.floor(minutes / 60);
  const remMinutes = minutes % 60;
  return `${hours}h ${remMinutes}m`;
}

function elapsedMs(sinceUtc, nowMs) {
  const since = Date.parse(sinceUtc);
  if (Number.isNaN(since)) return 0;
  return nowMs - since;
}

// One live section (Running / Waiting for slot / Queued). `showStage` hides the Step column for queued jobs.
function LiveSection({ titleKey, hint, items, showStage, nowMs, t }) {
  return (
    <section style={{ marginBottom: '1.5rem' }}>
      <h3 style={{ fontSize: 'var(--font-size-sm)', textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-secondary)', marginBottom: '0.5rem' }}>
        {t(titleKey)} <span style={{ opacity: 0.7 }}>({items.length})</span>
      </h3>
      {hint && <p style={{ color: 'var(--color-text-secondary)', margin: '0 0 0.5rem', fontSize: 'var(--font-size-sm)' }}>{hint}</p>}
      {items.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('live.none')}</p>
      ) : (
        <table className="data-table">
          <thead>
            <tr>
              <th>{t('live.document')}</th>
              {showStage && <th>{t('live.step')}</th>}
              <th style={{ width: '10rem' }}>{t('live.inState')}</th>
              <th style={{ width: '9rem' }}>{t('live.job')}</th>
            </tr>
          </thead>
          <tbody>
            {items.map((item) => (
              <tr key={item.jobId}>
                <td style={{ maxWidth: '32rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.sourceUrl}>{item.sourceUrl}</td>
                {showStage && <td>{item.stage ? stageLabel(item.stage) : '—'}</td>}
                <td><span className="mono">{formatElapsed(elapsedMs(item.stateSinceUtc, nowMs))}</span></td>
                <td><CopyableId value={item.jobId} truncateLen={12} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}

function IngestionLiveView() {
  const { t } = useTranslation();
  const { apiClient } = useAuth();
  const [snapshot, setSnapshot] = useState({ running: [], waitingForSlot: [], queued: [] });
  const [subjectId, setSubjectId] = useState('');
  const [subjects, setSubjects] = useState([]);
  const [error, setError] = useState(null);
  const [loading, setLoading] = useState(true);
  const [nowMs, setNowMs] = useState(Date.now());
  // Keep the latest subject filter available to the polling loop without re-creating the interval each tick.
  const subjectRef = useRef(subjectId);
  subjectRef.current = subjectId;

  const load = useCallback(async () => {
    try {
      const resp = await apiClient.getIngestionLive(subjectRef.current || null);
      setSnapshot({
        running: resp?.running || [],
        waitingForSlot: resp?.waitingForSlot || [],
        queued: resp?.queued || []
      });
      setError(null);
    } catch (err) {
      setError(err?.message || 'Failed to load live ingestion snapshot');
    } finally {
      setLoading(false);
    }
  }, [apiClient]);

  // Poll the snapshot on an interval, and reload immediately when the subject filter changes.
  useEffect(() => {
    load();
    const id = setInterval(load, POLL_MS);
    return () => clearInterval(id);
  }, [load, subjectId]);

  // Tick the clock so the "time in state" columns advance smoothly between polls.
  useEffect(() => {
    const id = setInterval(() => setNowMs(Date.now()), TICK_MS);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  const total = snapshot.running.length + snapshot.waitingForSlot.length + snapshot.queued.length;

  return (
    <div>
      <PageHeader title={t('live.title')} subtitle={t('live.subtitle')} />
      <div className="filter-bar">
        <div className="field">
          <label htmlFor="live-subject" className="has-tip" title={t('live.subjectTip')}>{t('jobs.subject', 'Subject')}</label>
          <select id="live-subject" value={subjectId} onChange={(e) => setSubjectId(e.target.value)} title={t('live.subjectTip')}>
            <option value="">{t('jobs.allSubjects', 'All subjects')}</option>
            {subjects.map((s) => <option key={s.id} value={s.id}>{s.displayName || s.name || s.id}</option>)}
          </select>
        </div>
        <div className="field" style={{ alignSelf: 'flex-end', color: 'var(--color-text-secondary)', fontSize: 'var(--font-size-sm)' }}>
          {t('live.autoRefresh')}
        </div>
      </div>

      {error && <ErrorBanner message={error} onRetry={load} onDismiss={() => setError(null)} />}

      {loading ? (
        <div className="table-loading"><div className="loading-spinner" /></div>
      ) : total === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('live.idle')}</p>
      ) : (
        <>
          <LiveSection titleKey="live.running" hint={t('live.runningHint')} items={snapshot.running} showStage nowMs={nowMs} t={t} />
          <LiveSection titleKey="live.waiting" hint={t('live.waitingHint')} items={snapshot.waitingForSlot} showStage nowMs={nowMs} t={t} />
          <LiveSection titleKey="live.queued" hint={t('live.queuedHint')} items={snapshot.queued} showStage={false} nowMs={nowMs} t={t} />
        </>
      )}
    </div>
  );
}

export default IngestionLiveView;
