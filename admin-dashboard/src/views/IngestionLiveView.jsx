import { useState, useEffect, useCallback, useRef } from 'react';
import { useTranslation } from 'react-i18next';
import { useAuth } from '../context/AuthContext';
import { normalizeList } from '../utils/api';
import PageHeader from '../components/PageHeader';
import ErrorBanner from '../components/ErrorBanner';
import CopyableId from '../components/CopyableId';
import ActionMenu from '../components/ActionMenu';
import ConfirmModal from '../components/ConfirmModal';
import Modal from '../components/Modal';
import FollowLogsModal from '../components/FollowLogsModal';
import JobPerformanceModal from '../components/JobPerformanceModal';
import { stageColor, stageLabel } from '../utils/ingestionActivity';

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

// A colored pill for the pipeline step, tinted with the shared per-stage color used across the dashboard.
function StageBadge({ stage }) {
  if (!stage) return <span style={{ color: 'var(--color-text-secondary)' }}>—</span>;
  const color = stageColor(stage);
  return (
    <span style={{
      display: 'inline-block', padding: '0.1rem 0.5rem', borderRadius: '999px',
      fontSize: 'var(--font-size-xs, 0.75rem)', fontWeight: 600, lineHeight: 1.5,
      color, background: `${color}22`, border: `1px solid ${color}55`, whiteSpace: 'nowrap'
    }}>
      {stageLabel(stage)}
    </span>
  );
}

// Shared column widths so the Step / Time-in-state / Job / Actions columns line up across all three tables.
// The Document column has no fixed width: with table-layout:fixed it takes the remaining space and its cell
// truncates the (often long) link with an ellipsis rather than widening the table off-screen.
function LiveColgroup() {
  return (
    <colgroup>
      <col />
      <col style={{ width: '12rem' }} />
      <col style={{ width: '7rem' }} />
      <col style={{ width: '8rem' }} />
      <col style={{ width: '3rem' }} />
    </colgroup>
  );
}

function LiveSection({ titleKey, hint, items, nowMs, t, actionsFor }) {
  return (
    <section style={{ marginBottom: '1.5rem' }}>
      <h3 style={{ fontSize: 'var(--font-size-sm)', textTransform: 'uppercase', letterSpacing: '0.04em', color: 'var(--color-text-secondary)', marginBottom: '0.5rem' }}>
        {t(titleKey)} <span style={{ opacity: 0.7 }}>({items.length})</span>
      </h3>
      {hint && <p style={{ color: 'var(--color-text-secondary)', margin: '0 0 0.5rem', fontSize: 'var(--font-size-sm)' }}>{hint}</p>}
      {items.length === 0 ? (
        <p style={{ color: 'var(--color-text-secondary)' }}>{t('live.none')}</p>
      ) : (
        <div style={{ overflowX: 'auto', maxWidth: '100%' }}>
          <table className="data-table" style={{ tableLayout: 'fixed', width: '100%' }}>
            <LiveColgroup />
            <thead>
              <tr>
                <th>{t('live.document')}</th>
                <th>{t('live.step')}</th>
                <th>{t('live.inState')}</th>
                <th>{t('live.job')}</th>
                <th aria-label={t('common.actions')} />
              </tr>
            </thead>
            <tbody>
              {items.map((item) => (
                <tr key={item.jobId}>
                  <td style={{ maxWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={item.sourceUrl}>{item.sourceUrl}</td>
                  <td><StageBadge stage={item.stage} /></td>
                  <td><span className="mono">{formatElapsed(elapsedMs(item.stateSinceUtc, nowMs))}</span></td>
                  <td><CopyableId value={item.jobId} truncateLen={12} /></td>
                  <td style={{ textAlign: 'right' }}><ActionMenu items={actionsFor(item)} /></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
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
  const [modal, setModal] = useState(null);
  const [notice, setNotice] = useState('');
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

  useEffect(() => {
    load();
    const id = setInterval(load, POLL_MS);
    return () => clearInterval(id);
  }, [load, subjectId]);

  useEffect(() => {
    const id = setInterval(() => setNowMs(Date.now()), TICK_MS);
    return () => clearInterval(id);
  }, []);

  useEffect(() => {
    apiClient.list('subjects').then((r) => setSubjects(normalizeList(r).items)).catch(() => {});
  }, [apiClient]);

  const restart = async (jobId) => { setModal(null); await apiClient.restartJob(jobId); await load(); };
  const stop = async (jobId) => { setModal(null); await apiClient.stopJob(jobId); await load(); };
  // Deletion cascades in the background (202 Accepted): close the confirm, surface the background notice, refresh.
  const remove = async (jobId) => { setModal(null); setNotice(t('jobs.deleteBackground')); await apiClient.deleteJob(jobId); await load(); };

  // A live entry maps to a job-like object the shared job modals/actions understand (they key off id/status/stage).
  const asJob = (item, status) => ({ id: item.jobId, status, stage: item.stage, sourceUrl: item.sourceUrl });

  const actionsFor = (status) => (item) => {
    const job = asJob(item, status);
    return [
      { key: 'follow', label: t('jobs.followLogs'), tip: 'Watch this job’s stage log live, auto-refreshing until it finishes.', onClick: () => setModal({ type: 'follow', job }) },
      { key: 'performance', label: t('jobs.viewPerformance', 'View Performance'), tip: 'Visualize where this job has spent time — a bar per stage sized by its duration.', onClick: () => setModal({ type: 'performance', job }) },
      { key: 'restart', label: t('jobs.restart', 'Restart Job'), tip: 'Re-queue this job to run again from the beginning.', onClick: () => setModal({ type: 'restart', job }) },
      { key: 'stop', label: t('jobs.stop'), tip: 'Cancel this job. Already-completed stages are kept.', danger: true, onClick: () => setModal({ type: 'stop', job }) },
      { key: 'delete', label: t('jobs.delete'), tip: 'Delete this job and cascade-remove its graph nodes, indexed chunks, and logs.', danger: true, onClick: () => setModal({ type: 'delete', job }) }
    ];
  };

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
          <LiveSection titleKey="live.running" hint={t('live.runningHint')} items={snapshot.running} nowMs={nowMs} t={t} actionsFor={actionsFor('Processing')} />
          <LiveSection titleKey="live.waiting" hint={t('live.waitingHint')} items={snapshot.waitingForSlot} nowMs={nowMs} t={t} actionsFor={actionsFor('Processing')} />
          <LiveSection titleKey="live.queued" hint={t('live.queuedHint')} items={snapshot.queued} nowMs={nowMs} t={t} actionsFor={actionsFor('Queued')} />
        </>
      )}

      {modal?.type === 'follow' && <FollowLogsModal job={modal.job} onClose={() => setModal(null)} />}
      {modal?.type === 'performance' && <JobPerformanceModal job={modal.job} onClose={() => setModal(null)} />}
      {modal?.type === 'restart' && (
        <ConfirmModal title={t('jobs.restart', 'Restart Job')} message={t('jobs.restartConfirm', 'Re-queue this job to run again from the beginning?')}
          confirmLabel={t('jobs.restart', 'Restart Job')} onConfirm={() => restart(modal.job.id)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'stop' && (
        <ConfirmModal title={t('jobs.stop')} message={t('jobs.stopConfirm')} danger
          confirmLabel={t('jobs.stop')} onConfirm={() => stop(modal.job.id)} onClose={() => setModal(null)} />
      )}
      {modal?.type === 'delete' && (
        <ConfirmModal title={t('jobs.delete')} message={t('jobs.deleteConfirm')} danger
          confirmLabel={t('common.delete')} onConfirm={() => remove(modal.job.id)} onClose={() => setModal(null)} />
      )}
      {notice && (
        <Modal title={t('common.notice')} size="sm" onClose={() => setNotice('')}
          footer={<button type="button" className="button-primary" onClick={() => setNotice('')}>{t('common.close')}</button>}>
          <p className="confirm-text">{notice}</p>
        </Modal>
      )}
    </div>
  );
}

export default IngestionLiveView;
